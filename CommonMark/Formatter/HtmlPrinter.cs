﻿using CommonMark.Parser;
using CommonMark.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace CommonMark.Formatter
{
    internal static class HtmlPrinter
    {
        private static readonly char[] EscapeHtmlCharacters = new[] { '&', '<', '>', '\"' };

        /// <summary>
        /// Escapes special HTML characters.
        /// </summary>
        /// <remarks>Orig: escape_html(inp, preserve_entities)</remarks>
        private static void EscapeHtml(string input, bool preserveEntities, System.IO.TextWriter target)
        {
            int pos = 0;
            int lastPos = 0;
            int match;
            char[] buffer = null;

            while ((pos = input.IndexOfAny(EscapeHtmlCharacters, lastPos)) != -1)
            {
                if (buffer == null)
                    buffer = input.ToCharArray();

                target.Write(buffer, lastPos, pos - lastPos);
                lastPos = pos + 1;

                switch (buffer[pos])
                {
                    case '<':
                        target.Write("&lt;");
                        break;
                    case '>':
                        target.Write("&gt;");
                        break;
                    case '&':
                        if (preserveEntities && 0 != (match = Scanner.scan_entity(input, pos, input.Length - pos)))
                            target.Write('&');
                        else
                            target.Write("&amp;");
                        break;
                    case '"':
                        target.Write("&quot;");
                        break;
                }
            }

            if (buffer == null)
                target.Write(input);

            target.Write(buffer, lastPos, input.Length - lastPos);
        }

        /// <summary>
        /// Escapes special HTML characters.
        /// </summary>
        /// <remarks>Orig: escape_html(inp, preserve_entities)</remarks>
        private static void EscapeHtml(StringContent inp, bool preserveEntities, System.IO.TextWriter target)
        {
            int pos;
            int lastPos;
            char[] buffer = null;

            var parts = inp.RetrieveParts();
            for (var i = parts.Offset; i < parts.Offset + parts.Count; i++)
            {
                var part = parts.Array[i];

                if (buffer == null || buffer.Length < part.Length)
                    buffer = new char[part.Length];
                
                part.Source.CopyTo(part.StartIndex, buffer, 0, part.Length);

                lastPos = pos = part.StartIndex;
                while ((pos = part.Source.IndexOfAny(EscapeHtmlCharacters, lastPos, part.Length - lastPos + part.StartIndex)) != -1)
                {
                    target.Write(buffer, lastPos - part.StartIndex, pos - lastPos);
                    lastPos = pos + 1;

                    switch (part.Source[pos])
                    {
                        case '<':
                            target.Write("&lt;");
                            break;
                        case '>':
                            target.Write("&gt;");
                            break;
                        case '&':
                            // note that here it is assumed that the entity will be completely within this one part
                            if (preserveEntities && 0 != Scanner.scan_entity(part.Source, pos, part.Length - pos + part.StartIndex))
                                target.Write('&');
                            else
                                target.Write("&amp;");
                            break;
                        case '"':
                            target.Write("&quot;");
                            break;
                    }
                }

                target.Write(buffer, lastPos - part.StartIndex, part.Length - lastPos + part.StartIndex);
            }
        }

        /// <summary>
        /// Adds a newline if the writer does not currently end with a newline.
        /// </summary>
        /// <remarks>Orig: cr</remarks>
        private static void EnsureNewlineEnding(HtmlTextWriter writer)
        {
            if (!writer.EndsWithNewline)
                writer.WriteLine();
        }

        /// <summary>
        /// Convert a block list to HTML.  Returns 0 on success, and sets result.
        /// </summary>
        /// <remarks>Orig: blocks_to_html</remarks>
        public static void BlocksToHtml(System.IO.TextWriter writer, Block b, bool tight)
        {
            using (var wrapper = new HtmlTextWriter(writer))
                BlocksToHtmlInner(wrapper, b, tight);
        }

        /// <remarks>Orig: blocks_to_html_inner</remarks>
        private static void BlocksToHtmlInner(HtmlTextWriter writer, Block b, bool tight)
        {
            string tag;
            while (b != null)
            {
                switch (b.Tag)
                {
                    case BlockTag.Document:
                        BlocksToHtmlInner(writer, b.FirstChild, false);
                        break;

                    case BlockTag.Paragraph:
                        if (tight)
                        {
                            InlinesToHtml(writer, b.InlineContent);
                        }
                        else
                        {
                            EnsureNewlineEnding(writer);
                            writer.Write("<p>");
                            InlinesToHtml(writer, b.InlineContent);
                            writer.WriteLine("</p>");
                        }
                        break;

                    case BlockTag.BlockQuote:
                        EnsureNewlineEnding(writer);
                        writer.WriteLine("<blockquote>");
                        BlocksToHtmlInner(writer, b.FirstChild, false);
                        writer.WriteLine("</blockquote>");
                        break;

                    case BlockTag.ListItem:
                        EnsureNewlineEnding(writer);
                        writer.Write("<li>");
                        using (var sb = new System.IO.StringWriter())
                        using (var sbw = new HtmlTextWriter(sb))
                        {
                            BlocksToHtmlInner(sbw, b.FirstChild, tight);
                            sbw.Flush();
                            writer.Write(sb.ToString().TrimEnd());
                        }
                        writer.WriteLine("</li>");
                        break;

                    case BlockTag.List:
                        // make sure a list starts at the beginning of the line:
                        EnsureNewlineEnding(writer);
                        var data = b.Attributes.ListData;
                        tag = data.ListType == ListType.Bullet ? "ul" : "ol";
                        writer.Write("<" + tag);
                        if (data.Start != 1)
                            writer.Write(" start=\"" + data.Start.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"");
                        writer.WriteLine(">");
                        BlocksToHtmlInner(writer, b.FirstChild, data.IsTight);
                        writer.WriteLine("</" + tag + ">");
                        break;

                    case BlockTag.AtxHeader:
                    case BlockTag.SETextHeader:
                        tag = "h" + b.Attributes.HeaderLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        EnsureNewlineEnding(writer);
                        writer.Write("<" + tag + ">");
                        InlinesToHtml(writer, b.InlineContent);
                        writer.WriteLine("</" + tag + ">");
                        break;

                    case BlockTag.IndentedCode:
                        EnsureNewlineEnding(writer);
                        writer.Write("<pre><code>");
                        EscapeHtml(b.StringContent, false, writer);
                        writer.WriteLine("</code></pre>");
                        break;

                    case BlockTag.FencedCode:
                        EnsureNewlineEnding(writer);
                        writer.Write("<pre><code");
                        if (b.Attributes.FencedCodeData.Info.Length > 0)
                        {
                            string[] info_words = b.Attributes.FencedCodeData.Info.Split(new[] { ' ' });
                            writer.Write(" class=\"language-");
                            EscapeHtml(info_words[0], true, writer);
                            writer.Write("\"");
                        }
                        writer.Write(">");
                        EscapeHtml(b.StringContent, false, writer);
                        writer.WriteLine("</code></pre>");
                        break;

                    case BlockTag.HtmlBlock:
                        b.StringContent.WriteTo(writer);
                        break;

                    case BlockTag.HorizontalRuler:
                        writer.WriteLine("<hr />");
                        break;

                    case BlockTag.ReferenceDefinition:
                        break;

                    default:
                        throw new CommonMarkException("Block type " + b.Tag + " is not supported.", b);
                }
                b = b.Next;
            }
        }

        // Convert an inline list to HTML.  Returns 0 on success, and sets result.
        /// <summary>
        /// </summary>
        /// <remarks>Orig: inlines_to_html</remarks>
        public static void InlinesToHtml(HtmlTextWriter writer, Inline ils)
        {
            while (ils != null)
            {
                switch (ils.Tag)
                {
                    case InlineTag.String:
                        EscapeHtml(ils.Content.Literal, false, writer);
                        break;

                    case InlineTag.LineBreak:
                        writer.WriteLine("<br />");
                        break;

                    case InlineTag.SoftBreak:
                        writer.WriteLine();
                        break;

                    case InlineTag.Code:
                        writer.Write("<code>");
                        EscapeHtml(ils.Content.Literal, false, writer);
                        writer.Write("</code>");
                        break;

                    case InlineTag.RawHtml:
                    case InlineTag.Entity:
                        writer.Write(ils.Content.Literal);
                        break;

                    case InlineTag.Link:
                        writer.Write("<a href=\"");
                        EscapeHtml(ils.Content.Linkable.Url, true, writer);
                        writer.Write('\"');
                        if (ils.Content.Linkable.Title.Length > 0)
                        {
                            writer.Write(" title=\"");
                            EscapeHtml(ils.Content.Linkable.Title, true, writer);
                            writer.Write('\"');
                        }
                        
                        writer.Write('>');
                        InlinesToHtml(writer, ils.Content.Linkable.Label);
                        writer.Write("</a>");
                        break;

                    case InlineTag.Image:
                        writer.Write("<img src=\"");
                        EscapeHtml(ils.Content.Linkable.Url, true, writer);
                        writer.Write("\" alt=\"");
                        using (var sb = new System.IO.StringWriter())
                        using (var sbw = new HtmlTextWriter(sb))
                        {
                            InlinesToHtml(sbw, ils.Content.Linkable.Label);
                            sbw.Flush();
                            EscapeHtml(sb.ToString(), false, writer);
                        }
                        writer.Write("\"");
                        if (ils.Content.Linkable.Title.Length > 0)
                        {
                            writer.Write(" title=\"");
                            EscapeHtml(ils.Content.Linkable.Title, true, writer);
                            writer.Write("\"");
                        }
                        writer.Write(" />");
                        break;

                    case InlineTag.Strong:
                        writer.Write("<strong>");
                        InlinesToHtml(writer, ils.Content.Inlines);
                        writer.Write("</strong>");
                        break;

                    case InlineTag.Emphasis:
                        writer.Write("<em>");
                        InlinesToHtml(writer, ils.Content.Inlines);
                        writer.Write("</em>");
                        break;

                    default:
                        throw new CommonMarkException("Inline type " + ils.Tag + " is not supported.", ils);
                }
                ils = ils.Next;
            }
        }

    }
}
