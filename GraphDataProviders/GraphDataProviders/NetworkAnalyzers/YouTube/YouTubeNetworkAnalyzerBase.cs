
//  Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Xml;
using System.Net;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Research.CommunityTechnologies.AppLib;
using Microsoft.Research.CommunityTechnologies.XmlLib;

namespace Microsoft.NodeXL.GraphDataProviders.YouTube
{
//*****************************************************************************
//  Class: YouTubeNetworkAnalyzerBase
//
/// <summary>
/// Base class for classes that analyze a YouTube network.
/// </summary>
//*****************************************************************************

public abstract class YouTubeNetworkAnalyzerBase : HttpNetworkAnalyzerBase
{
    //*************************************************************************
    //  Constructor: YouTubeNetworkAnalyzerBase()
    //
    /// <summary>
    /// Initializes a new instance of the <see
    /// cref="YouTubeNetworkAnalyzerBase" /> class.
    /// </summary>
    //*************************************************************************

    public YouTubeNetworkAnalyzerBase()
    {
        // (Do nothing.)

        AssertValid();
    }

    //*************************************************************************
    //  Method: CreateXmlNamespaceManager()
    //
    /// <summary>
    /// Creates an XmlNamespaceManager object appropriate for use with an XML
    /// document returned by YouTube.
    /// </summary>
    ///
    /// <param name="oXmlDocument">
    /// The XML document returned by YouTube.
    /// </param>
    ///
    /// <returns>
    /// An XmlNamespaceManager object appropriate for use with the XML
    /// document.  See the code for the namespace prefixes.
    /// </returns>
    //*************************************************************************

    public static XmlNamespaceManager
    CreateXmlNamespaceManager
    (
        XmlDocument oXmlDocument
    )
    {
        Debug.Assert(oXmlDocument != null);

        XmlNamespaceManager oXmlNamespaceManager =
            new XmlNamespaceManager(oXmlDocument.NameTable);

        oXmlNamespaceManager.AddNamespace("a", AtomNamespaceUri);
        oXmlNamespaceManager.AddNamespace("yt", YouTubeNamespaceUri);
        oXmlNamespaceManager.AddNamespace("media", MediaNamespaceUri);
        oXmlNamespaceManager.AddNamespace("gd", GoogleNamespaceUri);

        oXmlNamespaceManager.AddNamespace("openSearch",
            OpenSearchNamespaceUri);

        return (oXmlNamespaceManager);
    }

    //*************************************************************************
    //  Method: EnumerateXmlNodes()
    //
    /// <summary>
    /// Get an XmlDocument from an URL, then enumerates a specified set of
    /// child nodes.
    /// </summary>
    ///
    /// <param name="sUrl">
    /// The URL to get the document from.  Can include URL parameters.
    /// </param>
    ///
    /// <param name="sXPath">
    /// The XPath of the child nodes to enumerate.  Sample:
    /// "feed/entry".
    /// </param>
    ///
    /// <param name="iMaximumXmlNodes">
    /// Maximum number of child nodes to return, or Int32.MaxValue for no
    /// limit.
    /// </param>
    ///
    /// <param name="bSkipMostPage1Errors">
    /// If true, most page-1 errors are skipped over.  If false, they are
    /// rethrown.  (Errors that occur on page 2 and above are always skipped.)
    /// </param>
    ///
    /// <param name="oRequestStatistics">
    /// A <see cref="RequestStatistics" /> object that is keeping track of
    /// requests made while getting the network.
    /// </param>
    //*************************************************************************

    protected IEnumerable<XmlNode>
    EnumerateXmlNodes
    (
        String sUrl,
        String sXPath,
        Int32 iMaximumXmlNodes,
        Boolean bSkipMostPage1Errors,
        RequestStatistics oRequestStatistics
    )
    {
        Debug.Assert( !String.IsNullOrEmpty(sUrl) );
        Debug.Assert( !String.IsNullOrEmpty(sXPath) );
        Debug.Assert(iMaximumXmlNodes > 0);
        Debug.Assert(oRequestStatistics != null);

        AssertValid();

        Int32 iStartIndex = 1;
        Int32 iMaxResults = 50;
        Int32 iXmlNodesEnumerated = 0;

        while (true)
        {
            if (iStartIndex > 1)
            {
                ReportProgress("Getting page starting with item "
                    + iStartIndex);
            }

            String sUrlWithPagination = String.Format(
            
                "{0}{1}start-index={2}&max-results={3}"
                ,
                sUrl,
                sUrl.IndexOf('?') == -1 ? '?' : '&',
                iStartIndex,
                iMaxResults
                );

            XmlDocument oXmlDocument;
            XmlNamespaceManager oXmlNamespaceManager;

            try
            {
                oXmlDocument = GetXmlDocument(sUrlWithPagination,
                    oRequestStatistics, out oXmlNamespaceManager);
            }
            catch (Exception oException)
            {
                if ( !ExceptionIsWebOrXml(oException) )
                {
                    throw oException;
                }

                if (iStartIndex > 1 || bSkipMostPage1Errors)
                {
                    // Always skip errors on page 2 and above.

                    yield break;
                }

                throw (oException);
            }

            XmlNodeList oXmlNodesThisPage = oXmlDocument.SelectNodes(sXPath,
                oXmlNamespaceManager);

            Int32 iXmlNodesThisPage = oXmlNodesThisPage.Count;

            if (iXmlNodesThisPage == 0)
            {
                yield break;
            }

            for (Int32 i = 0; i < iXmlNodesThisPage; i++)
            {
                yield return ( oXmlNodesThisPage[i] );

                iXmlNodesEnumerated++;

                if (iXmlNodesEnumerated == iMaximumXmlNodes)
                {
                    yield break;
                }
            }

            // The next page, if there is one, is obtained from a link tag that
            // looks something like this:
            //
            // <link rel="next" ... href="http://gdata.youtube.com/feeds/...?
            // start-index=26&max-results=25"/>

            String sHRef;

            if ( !XmlUtil2.SelectSingleNode(oXmlDocument,
                "a:feed/a:link[@rel='next']/@href", oXmlNamespaceManager,
                false, out sHRef) )
            {
                yield break;
            }

            const String Pattern = @"start-index=(?<NextStartIndex>\d+)";
            Regex oRegex = new Regex(Pattern);
            Match oMatch = oRegex.Match(sHRef);

            if (!oMatch.Success
                ||
                !MathUtil.TryParseCultureInvariantInt32(
                    oMatch.Groups["NextStartIndex"].Value, out iStartIndex)
                )
            {
                yield break;
            }

            // Get the next page...
        }
    }

    //*************************************************************************
    //  Method: TryGetXmlDocument()
    //
    /// <summary>
    /// Attempts to get an XML document from an URL.
    /// </summary>
    ///
    /// <param name="sUrl">
    /// The URL to get the document from.  Can include URL parameters.
    /// </param>
    ///
    /// <param name="oRequestStatistics">
    /// A <see cref="RequestStatistics" /> object that is keeping track of
    /// requests made while getting the network.
    /// </param>
    ///
    /// <param name="oXmlDocument">
    /// Where the XmlDocument gets stored if true is returned.
    /// </param>
    ///
    /// <param name="oXmlNamespaceManager">
    /// Where the XmlNamespaceManager for the XmlDocument gets stored if true
    /// is returned.
    /// </param>
    ///
    /// <returns>
    /// true if the XmlDocument was obtained, false if an error occurred.
    /// </returns>
    ///
    /// <remarks>
    /// If an error occurs, false is returned.  A WebException or XmlException
    /// is never rethrown.
    /// </remarks>
    //*************************************************************************

    protected Boolean
    TryGetXmlDocument
    (
        String sUrl,
        RequestStatistics oRequestStatistics,
        out XmlDocument oXmlDocument,
        out XmlNamespaceManager oXmlNamespaceManager
    )
    {
        Debug.Assert( !String.IsNullOrEmpty(sUrl) );
        Debug.Assert(oRequestStatistics != null);
        AssertValid();

        oXmlDocument = null;
        oXmlNamespaceManager = null;

        try
        {
            oXmlDocument = GetXmlDocument(sUrl, oRequestStatistics,
                out oXmlNamespaceManager);

            return (true);
        }
        catch (Exception oException)
        {
            if ( !ExceptionIsWebOrXml(oException) )
            {
                throw oException;
            }

            return (false);
        }
    }

    //*************************************************************************
    //  Method: GetXmlDocument()
    //
    /// <summary>
    /// Gets an XML document from an URL.
    /// </summary>
    ///
    /// <param name="sUrl">
    /// The URL to get the document from.  Can include URL parameters.
    /// </param>
    ///
    /// <param name="oRequestStatistics">
    /// A <see cref="RequestStatistics" /> object that is keeping track of
    /// requests made while getting the network.
    /// </param>
    ///
    /// <param name="oXmlNamespaceManager">
    /// Where the XmlNamespaceManager for the XmlDocument gets stored if true
    /// is returned.
    /// </param>
    ///
    /// <returns>
    /// The XmlDocument from the URL.
    /// </returns>
    ///
    /// <remarks>
    /// If an error occurs, an exception is thrown.
    /// </remarks>
    //*************************************************************************

    protected XmlDocument
    GetXmlDocument
    (
        String sUrl,
        RequestStatistics oRequestStatistics,
        out XmlNamespaceManager oXmlNamespaceManager
    )
    {
        Debug.Assert( !String.IsNullOrEmpty(sUrl) );
        Debug.Assert(oRequestStatistics != null);
        AssertValid();

        // Always request API version 2.

        String sUrlWithVersion = String.Format(
            
            "{0}{1}v=2"
            ,
            sUrl,
            sUrl.IndexOf('?') == -1 ? '?' : '&'
            );

        XmlDocument oXmlDocument = GetXmlDocumentWithRetries(sUrlWithVersion,
            SpecialHttpStatusCodes, oRequestStatistics, null);

        oXmlNamespaceManager = CreateXmlNamespaceManager(oXmlDocument);

        return (oXmlDocument);
    }

    //*************************************************************************
    //  Method: EncodeUrlParameter()
    //
    /// <summary>
    /// Encodes an URL parameter for use with YouTube.
    /// </summary>
    ///
    /// <param name="urlParameter">
    /// The URL parameter to be encoded.  Can't be null.
    /// </param>
    ///
    /// <returns>
    /// The encoded parameter.
    /// </returns>
    //*************************************************************************

    protected String
    EncodeUrlParameter
    (
        String urlParameter
    )
    {
        Debug.Assert(urlParameter != null);

        // From the YouTube documentation:
        //
        // "To URL escape a string, convert each sequence of whitespace
        // characters to a single "+" (plus sign) and replace any other
        // nonalphanumeric characters with the hexadecimal encoding that
        // represents the value of that character."

        // Compress whitespace into a single space.

        urlParameter = Regex.Replace(urlParameter, "\\s+", " ");

        // Use .NET's URL encoding.

        urlParameter = UrlUtil.EncodeUrlParameter(urlParameter);

        // That converted each space to "%20".  Convert that to the plus sign
        // YouTube expects.

        urlParameter = urlParameter.Replace("%20", "+");

        return (urlParameter);
    }

    //*************************************************************************
    //  Method: AppendYouTubeDateGraphMLAttributeValue()
    //
    /// <summary>
    /// Appends a YouTube date GraphML-Attribute value to a vertex XML node. 
    /// </summary>
    ///
    /// <param name="oXmlNodeToSelectFrom">
    /// Node to select from.
    /// </param>
    /// 
    /// <param name="sXPath">
    /// XPath expression to YouTube date descendant of <paramref
    /// name="oXmlNodeToSelectFrom" />.
    /// </param>
    ///
    /// <param name="oXmlNamespaceManager">
    /// NamespaceManager to use, or null to not use one.
    /// </param>
    ///
    /// <param name="oGraphMLXmlDocument">
    /// GraphMLXmlDocument being populated.
    /// </param>
    ///
    /// <param name="oVertexXmlNode">
    /// The vertex XML node from <paramref name="oGraphMLXmlDocument" /> to add
    /// the GraphML attribute value to.
    /// </param>
    ///
    /// <param name="sGraphMLAttributeID">
    /// GraphML ID of the attribute.
    /// </param>
    ///
    /// <returns>
    /// true if the GraphML-Attribute was appended.
    /// </returns>
    ///
    /// <remarks>
    /// This method selects from <paramref name="oXmlNodeToSelectFrom" /> using
    /// the <paramref name="sXPath" /> expression.  If the selection is
    /// successful, the specified value (which should be a date in YouTube
    /// format) gets stored on <paramref name="oVertexXmlNode" /> as a Graph-ML
    /// Attribute.
    /// </remarks>
    //*************************************************************************

    protected Boolean
    AppendYouTubeDateGraphMLAttributeValue
    (
        XmlNode oXmlNodeToSelectFrom,
        String sXPath,
        XmlNamespaceManager oXmlNamespaceManager,
        GraphMLXmlDocument oGraphMLXmlDocument,
        XmlNode oVertexXmlNode,
        String sGraphMLAttributeID
    )
    {
        Debug.Assert(oXmlNodeToSelectFrom != null);
        Debug.Assert( !String.IsNullOrEmpty(sXPath) );
        Debug.Assert(oGraphMLXmlDocument != null);
        Debug.Assert(oVertexXmlNode != null);
        Debug.Assert( !String.IsNullOrEmpty(sGraphMLAttributeID) );
        AssertValid();

        String sYouTubeDate;

        if ( XmlUtil2.SelectSingleNode(oXmlNodeToSelectFrom, sXPath,
            oXmlNamespaceManager, false, out sYouTubeDate) )
        {
            oGraphMLXmlDocument.AppendGraphMLAttributeValue(oVertexXmlNode,
                sGraphMLAttributeID, FormatYouTubeDate(sYouTubeDate) );

            return (true);
        }

        return (false);
    }

    //*************************************************************************
    //  Method: FormatYouTubeDate()
    //
    /// <summary>
    /// Formats a date returned by YouTube.
    /// </summary>
    ///
    /// <param name="sYouTubeDate">
    /// The date received from YouTube.  Sample:
    /// "2008-04-29T21:42:41.000-07:00".
    /// </param>
    ///
    /// <returns>
    /// The formatted date.  Sample: "2008-04-29".
    /// </returns>
    //*************************************************************************

    protected String
    FormatYouTubeDate
    (
        String sYouTubeDate
    )
    {
        Debug.Assert(sYouTubeDate != null);

        if (sYouTubeDate.Length >= 10)
        {
            return ( sYouTubeDate.Substring(0, 10) );
        }

        return (sYouTubeDate);
    }


    //*************************************************************************
    //  Method: AssertValid()
    //
    /// <summary>
    /// Asserts if the object is in an invalid state.  Debug-only.
    /// </summary>
    //*************************************************************************

    // [Conditional("DEBUG")]

    public override void
    AssertValid()
    {
        base.AssertValid();

        // (Do nothing else.)
    }


    //*************************************************************************
    //  Protected constants
    //*************************************************************************

    /// HTTP status codes that have special meaning with YouTube.  When they
    /// occur, the requests are not retried.

    protected static readonly HttpStatusCode [] SpecialHttpStatusCodes =
        new HttpStatusCode[] {

            // Occurs when the a user or video isn't found.

            HttpStatusCode.NotFound,

            // Occurs when information that requires authentication is
            // requested.

            HttpStatusCode.Forbidden,
        };


    //*************************************************************************
    //  Protected fields
    //*************************************************************************

    /// Namespace URIs used by YouTube.

    protected const String GoogleNamespaceUri =
        "http://schemas.google.com/g/2005";
    ///
    protected const String YouTubeNamespaceUri =
        "http://gdata.youtube.com/schemas/2007";
    ///
    protected const String MediaNamespaceUri =
        "http://search.yahoo.com/mrss/";
    ///
    protected const String OpenSearchNamespaceUri =
        "http://a9.com/-/spec/opensearch/1.1/";
}

}
