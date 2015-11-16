// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.EntityModel
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Xml;
    using System.Xml.XPath;

    public interface ITripleSlashCommentParserContext
    {
        bool Normalize { get; set; }
        bool PreserveRawInlineComments { get; set; }
        Action<string> AddReferenceDelegate { get; set; }
    }

    public class TripleSlashCommentParserContext : ITripleSlashCommentParserContext
    {
        public bool Normalize { get; set; } = true;

        public bool PreserveRawInlineComments { get; set; }

        public Action<string> AddReferenceDelegate { get; set; }

    }

    public static class TripleSlashCommentParser
    {
        /// <summary>
        /// Get summary node out from triple slash comments
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="normalize"></param>
        /// <returns></returns>
        public static string GetSummary(string xml, ITripleSlashCommentParserContext context)
        {
            // Resolve <see cref> to @ syntax
            // Also support <seealso cref>
            string selector = "/member/summary";

            // Trim each line as a temp workaround
            var summary = GetSingleNode(xml, selector, context, (e) => null);
            return summary;
        }

        /// <summary>
        /// Get remarks node out from triple slash comments
        /// </summary>
        /// <remarks>
        /// <para>This is a sample of exception node</para>
        /// </remarks>
        /// <param name="xml"></param>
        /// <param name="normalize"></param>
        /// <returns></returns>
        public static string GetRemarks(string xml, ITripleSlashCommentParserContext context)
        {
            string selector = "/member/remarks";
            // Trim each line as a temp workaround
            var remarks = GetSingleNode(xml, selector, context, (e) => null);
            return remarks;
        }

        /// <summary>
        /// Get exceptions nodes out from triple slash comments
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="normalize"></param>
        /// <returns></returns>
        /// <exception cref="XmlException">This is a sample of exception node</exception>
        public static List<CrefInfo> GetExceptions(string xml, ITripleSlashCommentParserContext context)
        {
            string selector = "/member/exception";
            var result = GetMulitpleCrefInfo(xml, selector, context).ToList();
            if (result.Count == 0) return null;
            return result;
        }

        /// <summary>
        /// To get `see` tags out
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        /// <see cref="SpecIdHelper"/>
        /// <see cref="SourceSwitch"/>
        public static List<CrefInfo> GetSees(string xml, ITripleSlashCommentParserContext context)
        {
            var result = GetMulitpleCrefInfo(xml, "/member/see", context).ToList();
            if (result.Count == 0) return null;
            return result;
        }

        /// <summary>
        /// To get `seealso` tags out
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        /// <seealso cref="WaitForChangedResult"/>
        /// <seealso cref="http://google.com">ABCS</seealso>
        public static List<CrefInfo> GetSeeAlsos(string xml, ITripleSlashCommentParserContext context)
        {
            var result = GetMulitpleCrefInfo(xml, "/member/seealso", context).ToList();
            if (result.Count == 0) return null;
            return result;
        }

        /// <summary>
        /// To get `example` tags out
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        /// <example> 
        /// This sample shows how to call the <see cref="GetExceptions(string, ITripleSlashCommentParserContext)"/> method.
        /// <code>
        /// class TestClass  
        /// { 
        ///     static int Main()  
        ///     { 
        ///         return GetExceptions(null, null).Count(); 
        ///     } 
        /// } 
        /// </code> 
        /// </example>
        public static string GetExample(string xml, ITripleSlashCommentParserContext context)
        {
            // Resolve <see cref> to @ syntax
            // Also support <seealso cref>
            return GetSingleNode(xml, "/member/example", context, (e) => null);
        }

        public static string GetReturns(string xml, ITripleSlashCommentParserContext context)
        {
            // Resolve <see cref> to @ syntax
            // Also support <seealso cref>
            string selector = "/member/returns";
            return GetSingleNode(xml, selector, context, (e) => null);
        }

        public static string GetParam(string xml, string param, ITripleSlashCommentParserContext context)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            Debug.Assert(!string.IsNullOrEmpty(param));
            if (string.IsNullOrEmpty(param))
            {
                return null;
            }

            // Resolve <see cref> to @ syntax
            // Also support <seealso cref>
            string selector = "/member/param[@name='" + param + "']";

            return GetSingleNode(xml, selector, context, (e) => null);
        }

        public static string GetTypeParameter(string xml, string name, ITripleSlashCommentParserContext context)

        {
            if (string.IsNullOrEmpty(xml)) return null;
            Debug.Assert(!string.IsNullOrEmpty(name));
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Resolve <see cref> to @ syntax
            // Also support <seealso cref>
            string selector = "/member/typeparam[@name='" + name + "']";
            return GetSingleNode(xml, selector, context, (e) => null);
        }

        private static string ResolveInternalTags(string xml, string selector, ITripleSlashCommentParserContext context)
        {
            if (string.IsNullOrEmpty(xml) || context == null || context.PreserveRawInlineComments) return xml;
            return ResolveInternalTags(xml, selector, context.AddReferenceDelegate);
        }

        private static string ResolveInternalTags(string xml, string selector, Action<string> addReference)
        {
            if (string.IsNullOrEmpty(xml)) return xml;
            // Quick turnaround for badly formed XML comment
            if (xml.StartsWith("<!-- Badly formed XML comment ignored for member ")) return xml;
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                var nav = doc.CreateNavigator();
                ResolveSeeCref(nav, selector, addReference);
                ResolveSeeAlsoCref(nav, selector, addReference);
                ResolveParameterRef(nav);
                xml = doc.InnerXml;
            } catch {}
            return xml;
        }

        private static void ResolveParameterRef(XPathNavigator nav)
        {
            var paramRefs = nav.Select("//paramref").OfType<XPathNavigator>().ToArray();
            foreach (var paramRef in paramRefs)
            {
                var name = paramRef.SelectSingleNode("@name");
                if (name != null)
                {
                    // Convert paramref to italic
                    paramRef.InsertAfter("*" + name.Value + "*");
                }
            }

            foreach (var paramRef in paramRefs)
            {
                paramRef.DeleteSelf();
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="nav"></param>
        /// <param name="nodeSelector"></param>
        /// <returns></returns>
        private static void ResolveSeeAlsoCref(XPathNavigator nav, string nodeSelector, Action<string> addReference)
        {
            // Resolve <see cref> to @ syntax
            ResolveCrefLink(nav, nodeSelector + "//seealso", addReference);
        }

        private static void ResolveSeeCref(XPathNavigator nav, string nodeSelector, Action<string> addReference)
        {
            // Resolve <see cref> to @ syntax
            ResolveCrefLink(nav, nodeSelector + "//see", addReference);
        }

        private static void ResolveCrefLink(XPathNavigator nav, string nodeSelector, Action<string> addReference)
        {
            if (nav == null || string.IsNullOrEmpty(nodeSelector)) return;

            try
            {
                var iter = nav.Select(nodeSelector + "[@cref]");
                List<XPathNavigator> sees = new List<XPathNavigator>();
                foreach (XPathNavigator i in iter)
                {
                    var node = i.SelectSingleNode("@cref");
                    if (node != null)
                    {
                        var value = node.Value;

                        // Strict check is needed as value could be an invalid href, 
                        // e.g. !:Dictionary&lt;TKey, string&gt; when user manually changed the intellisensed generic type
                        if (LinkParser.CommentIdRegex.IsMatch(value))
                        {
                            value = value.Substring(2);
                            i.InsertAfter("@'" + value + "'");

                            sees.Add(i);
                            if (addReference != null)
                            {
                                addReference(value);
                            }
                        }
                        else
                        {
                            Logger.Log(LogLevel.Warning, $"Invalid cref value {value} found in triple-slash-comments, ignored.");
                        }
                    }
                }

                // on successful deleteself, i would point to its parent
                foreach (XPathNavigator i in sees)
                {
                    i.DeleteSelf();
                }
            }
            catch
            {
            }
        }

        private static IEnumerable<CrefInfo> GetMulitpleCrefInfo(string xml, string selector, ITripleSlashCommentParserContext context)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(selector) || context == null) yield break;
            xml = ResolveInternalTags(xml, selector, context);

            var iterator = SelectNodes(xml, selector);
            if (iterator == null) yield break;
            foreach (XPathNavigator nav in iterator)
            {
                string description = nav.Value;
                if (context?.Normalize ?? true) description = NormalizeContentFromTripleSlashComment(description);

                string exceptionType = nav.GetAttribute("cref", string.Empty);
                if (!string.IsNullOrEmpty(exceptionType))
                {
                    // Check if exception type is valid and trim prefix
                    if (LinkParser.CommentIdRegex.IsMatch(exceptionType))
                    {
                        exceptionType = exceptionType.Substring(2);
                        if (string.IsNullOrEmpty(description)) description = null;
                        yield return new CrefInfo { Description = description, Type = exceptionType };
                    }
                }
            }
        }

        private static XPathNodeIterator SelectNodes(string xml, string selector)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(selector)) return null;
            try
            {
                using (StringReader reader = new StringReader(xml))
                {
                    XPathDocument doc = new XPathDocument(reader);
                    var nav = doc.CreateNavigator();
                    return nav.Select(selector);
                }
            } catch (XmlException)
            {
                return null;
            }
        }

        private static string GetSingleNode(string xml, string selector, ITripleSlashCommentParserContext context, Func<Exception, string> errorHandler)
        {
            xml = ResolveInternalTags(xml, selector, context);
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(selector)) return xml;
            try
            {
                using (StringReader reader = new StringReader(xml))
                {
                    XPathDocument doc = new XPathDocument(reader);
                    var nav = doc.CreateNavigator();
                    var node = nav.SelectSingleNode(selector);
                    if (node == null)
                    {
                        // throw new ArgumentException(selector + " is not found");
                        return null;
                    }
                    else
                    {
                        // NOTE: use node.InnerXml instead of node.Value, to keep decorative nodes,
                        // e.g.
                        // <remarks><para>Value</para></remarks>
                        var output = node.InnerXml;
                        if (context?.Normalize ?? false) output = NormalizeContentFromTripleSlashComment(output);
                        return output;
                    }
                }
            }
            catch (Exception e)
            {
                if (errorHandler != null)
                {
                    return errorHandler(e);
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// The issue with GetXmlDocumentationXML is that it append /r/n and 4 spaces to the new line,
        /// which is considered as code in Markdown syntax
        /// </summary>
        /// <param name="content">The content from triple slash comment</param>
        /// <returns></returns>
        private static string NormalizeContentFromTripleSlashComment(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            StringBuilder builder = new StringBuilder();
            using (StringReader reader = new StringReader(content))
            {
                string line;
                // Trim spaces for each line, thus actually Tab to indent is not supported...while new line is kept
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    builder.AppendLine(line);
                }
            }

            // Trim again, e.g. <summary> always starts a new line and thus a \r\n is the first line
            return builder.ToString().Trim();
        }
    }
}
