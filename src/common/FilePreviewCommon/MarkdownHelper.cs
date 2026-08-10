// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text;

using Markdig;

namespace Microsoft.PowerToys.FilePreviewCommon
{
    public static class MarkdownHelper
    {
        private const string HtmlDoctype = "<!doctype html>";

        /// <summary>
        /// Markdown HTML header for light theme.
        /// </summary>
        private static readonly string HtmlLightHeader = "<!doctype html><style>body{width:100%;margin:0;font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,\"Helvetica Neue\",Arial,\"Noto Sans\",sans-serif,\"Apple Color Emoji\",\"Segoe UI Emoji\",\"Segoe UI Symbol\",\"Noto Color Emoji\";font-size:1rem;font-weight:400;line-height:1.5;color:#212529;text-align:left;background-color:#fff}.container{padding:5%}body img{max-width:100%;height:auto}body h1,body h2,body h3,body h4,body h5,body h6{margin-top:24px;margin-bottom:16px;font-weight:600;line-height:1.25}body h1,body h2{padding-bottom:.3em;border-bottom:1px solid #eaecef}body{font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Helvetica,Arial,sans-serif,Apple Color Emoji,Segoe UI Emoji}body h3{font-size:1.25em}body h4{font-size:1em}body h5{font-size:.875em}body h6{font-size:.85em;color:#6a737d}pre{font-family:SFMono-Regular,Consolas,Liberation Mono,Menlo,monospace;background-color:#f6f8fa;border-radius:3px;padding:16px;font-size:85%}a{color:#0366d6}strong{font-weight:600}em{font-style:italic}code{padding:.2em .4em;margin:0;font-size:85%;background-color:#f6f8fa;border-radius:3px}hr{border-color:#EEE -moz-use-text-color #FFF;border-style:solid none;border-width:.5px 0;margin:18px 0}table{display:block;width:100%;overflow:auto;border-spacing:0;border-collapse:collapse}tbody{display:table-row-group;vertical-align:middle;border-color:inherit;vertical-align:inherit;border-color:inherit}table tr{background-color:#fff;border-top:1px solid #c6cbd1}tr{display:table-row;vertical-align:inherit;border-color:inherit}table td,table th{padding:6px 13px;border:1px solid #dfe2e5}th{font-weight:600;display:table-cell;vertical-align:inherit;font-weight:bold;text-align:-internal-center}thead{display:table-header-group;vertical-align:middle;border-color:inherit}td{display:table-cell;vertical-align:inherit}code,pre,tt{font-family:SFMono-Regular,Menlo,Monaco,Consolas,\"Liberation Mono\",\"Courier New\",monospace;color:#24292e;overflow-x:auto}pre code{display:block;font-size:inherit;color:inherit;word-break:normal}blockquote{background-color:#fff;border-radius:3px;padding:15px;font-size:14px;display:block;margin-block-start:1em;margin-block-end:1em;margin-inline-start:40px;margin-inline-end:40px;padding:0 1em;color:#6a737d;border-left:.25em solid #dfe2e5}</style><body><div class=\"container\">";

        /// <summary>
        /// Markdown HTML header for dark theme.
        /// </summary>
        private static readonly string HtmlDarkHeader = "<!doctype html><style>body{width:100%;margin:0;font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,\"Helvetica Neue\",Arial,\"Noto Sans\",sans-serif,\"Apple Color Emoji\",\"Segoe UI Emoji\",\"Segoe UI Symbol\",\"Noto Color Emoji\";font-size:1rem;font-weight:400;line-height:1.5;color:#d4d4d4;text-align:left;background-color:#1e1e1e}.container{padding:5%}body img{max-width:100%;height:auto}body h1,body h2,body h3,body h4,body h5,body h6{margin-top:24px;margin-bottom:16px;font-weight:600;line-height:1.25}body h1,body h2{padding-bottom:.3em;border-bottom:1px solid #474747}body{font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Helvetica,Arial,sans-serif,Apple Color Emoji,Segoe UI Emoji}body h3{font-size:1.25em}body h4{font-size:1em}body h5{font-size:.875em}body h6{font-size:.85em;color:#d4d4d4}pre{font-family:SFMono-Regular,Consolas,Liberation Mono,Menlo,monospace;background-color:#161616;border-radius:3px;padding:16px;font-size:85%}a{color:#0366d6}strong{font-weight:600}em{font-style:italic}code{padding:.2em .4em;margin:0;font-size:85%;background-color:#161616;border-radius:3px}hr{border-color:#EEE -moz-use-text-color #FFF;border-style:solid none;border-width:.5px 0;margin:18px 0}table{display:block;width:100%;overflow:auto;border-spacing:0;border-collapse:collapse}tbody{display:table-row-group;vertical-align:middle;border-color:inherit;vertical-align:inherit;border-color:inherit}table tr{background-color:#1e1e1e;border-top:1px solid #c6cbd1}tr{display:table-row;vertical-align:inherit;border-color:inherit}table td,table th{padding:6px 13px;border:1px solid #474747}th{font-weight:600;display:table-cell;vertical-align:inherit;font-weight:bold;text-align:-internal-center}thead{display:table-header-group;vertical-align:middle;border-color:inherit}td{display:table-cell;vertical-align:inherit}code,pre,tt{font-family:SFMono-Regular,Menlo,Monaco,Consolas,\"Liberation Mono\",\"Courier New\",monospace;color:#d4d4d4;overflow-x:auto}pre code{display:block;font-size:inherit;color:inherit;word-break:normal}blockquote{background-color:#282828;border-radius:3px;padding:15px;font-size:14px;display:block;margin-block-start:1em;margin-block-end:1em;margin-inline-start:40px;margin-inline-end:40px;padding:0 1em;color:#d4d4d4;border-left:.25em solid #d4d4d4}</style><body><div class=\"container\">";

        /// <summary>
        /// Markdown HTML footer.
        /// </summary>
        private static readonly string HtmlFooter = "</div></body></html>";

        public static string MarkdownHtml(string fileContent, string theme, string filePath, ImagesBlockedCallBack imagesBlockedCallBack)
        {
            return MarkdownHtml(fileContent, theme, filePath, imagesBlockedCallBack, false, null);
        }

        public static string MarkdownHtml(string fileContent, string theme, string filePath, ImagesBlockedCallBack imagesBlockedCallBack, bool allowLocalImages, string? allowedBasePath)
        {
            string imageSourcePolicy = allowLocalImages ? "https://localmdimages" : "'none'";
            string contentSecurityPolicy = $"<meta http-equiv=\"Content-Security-Policy\" content=\"img-src {imageSourcePolicy};\">";
            string htmlHeader = (theme == "dark" ? HtmlDarkHeader : HtmlLightHeader).Insert(HtmlDoctype.Length, contentSecurityPolicy);

            // Extension to modify markdown AST.
            HTMLParsingExtension extension = new HTMLParsingExtension(imagesBlockedCallBack);
            extension.FilePath = Path.GetDirectoryName(filePath) ?? string.Empty;
            extension.AllowedBasePath = allowedBasePath ?? extension.FilePath;
            extension.AllowLocalImages = allowLocalImages;

            // if you have a string with double space, some people view it as a new line.
            // while this is against spec, even GH supports this. Technically looks like GH just trims whitespace
            // https://github.com/microsoft/PowerToys/issues/10354
            var softlineBreak = new Markdig.Extensions.Hardlines.SoftlineBreakAsHardlineExtension();

            MarkdownPipelineBuilder pipelineBuilder;
            pipelineBuilder = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseEmojiAndSmiley().UseYamlFrontMatter().UseMathematics();
            pipelineBuilder.Extensions.Add(extension);
            pipelineBuilder.Extensions.Add(softlineBreak);

            MarkdownPipeline pipeline = pipelineBuilder.Build();
            string parsedMarkdown = Markdown.ToHtml(fileContent, pipeline);

            parsedMarkdown = SanitizeRawImageTags(parsedMarkdown, extension, imagesBlockedCallBack, allowLocalImages);

            string markdownHTML = $"{htmlHeader}{parsedMarkdown}{HtmlFooter}";
            return markdownHTML;
        }

        private static string SanitizeRawImageTags(string html, HTMLParsingExtension extension, ImagesBlockedCallBack imagesBlockedCallBack, bool allowLocalImages)
        {
            StringBuilder? sanitized = null;
            int copyFrom = 0;
            int searchFrom = 0;

            while (true)
            {
                int tagStart = FindNextImageTag(html, searchFrom);
                if (tagStart < 0)
                {
                    break;
                }

                int tagEnd = FindTagEnd(html, tagStart + 4);
                if (tagEnd < 0)
                {
                    break;
                }

                sanitized ??= new StringBuilder(html.Length);
                sanitized.Append(html, copyFrom, tagStart - copyFrom);
                sanitized.Append(SanitizeRawImageTag(html.Substring(tagStart, tagEnd - tagStart + 1), extension, imagesBlockedCallBack, allowLocalImages));

                copyFrom = tagEnd + 1;
                searchFrom = copyFrom;
            }

            if (sanitized == null)
            {
                return html;
            }

            sanitized.Append(html, copyFrom, html.Length - copyFrom);
            return sanitized.ToString();
        }

        private static int FindNextImageTag(string html, int startIndex)
        {
            while (startIndex < html.Length)
            {
                int tagStart = html.IndexOf("<img", startIndex, StringComparison.OrdinalIgnoreCase);
                if (tagStart < 0)
                {
                    return -1;
                }

                int afterName = tagStart + 4;
                if (afterName == html.Length || char.IsWhiteSpace(html[afterName]) || html[afterName] == '/' || html[afterName] == '>')
                {
                    return tagStart;
                }

                startIndex = afterName;
            }

            return -1;
        }

        private static int FindTagEnd(string html, int startIndex)
        {
            char quote = '\0';
            for (int i = startIndex; i < html.Length; i++)
            {
                char current = html[i];
                if (quote != '\0')
                {
                    if (current == quote)
                    {
                        quote = '\0';
                    }
                }
                else if (current == '"' || current == '\'')
                {
                    quote = current;
                }
                else if (current == '>')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string SanitizeRawImageTag(string tag, HTMLParsingExtension extension, ImagesBlockedCallBack imagesBlockedCallBack, bool allowLocalImages)
        {
            StringBuilder sanitized = new StringBuilder(tag.Length);
            sanitized.Append(tag, 0, 4);

            int cursor = 4;
            int tagContentEnd = tag.Length - 1;
            while (cursor < tagContentEnd)
            {
                int segmentStart = cursor;
                while (cursor < tagContentEnd && char.IsWhiteSpace(tag[cursor]))
                {
                    cursor++;
                }

                if (cursor >= tagContentEnd || tag[cursor] == '/')
                {
                    sanitized.Append(tag, segmentStart, tagContentEnd - segmentStart);
                    break;
                }

                int nameStart = cursor;
                while (cursor < tagContentEnd &&
                       !char.IsWhiteSpace(tag[cursor]) &&
                       tag[cursor] != '=' &&
                       tag[cursor] != '/' &&
                       tag[cursor] != '>')
                {
                    cursor++;
                }

                if (cursor == nameStart)
                {
                    sanitized.Append(tag[cursor]);
                    cursor++;
                    continue;
                }

                string attributeName = tag.Substring(nameStart, cursor - nameStart);
                while (cursor < tagContentEnd && char.IsWhiteSpace(tag[cursor]))
                {
                    cursor++;
                }

                if (cursor >= tagContentEnd || tag[cursor] != '=')
                {
                    sanitized.Append(tag, segmentStart, cursor - segmentStart);
                    continue;
                }

                cursor++;
                while (cursor < tagContentEnd && char.IsWhiteSpace(tag[cursor]))
                {
                    cursor++;
                }

                int valueTokenStart = cursor;
                char valueQuote = '\0';
                int valueStart = cursor;
                int valueEnd;
                if (cursor < tagContentEnd && (tag[cursor] == '"' || tag[cursor] == '\''))
                {
                    valueQuote = tag[cursor];
                    valueStart = ++cursor;
                    while (cursor < tagContentEnd && tag[cursor] != valueQuote)
                    {
                        cursor++;
                    }

                    valueEnd = cursor;
                    if (cursor < tagContentEnd)
                    {
                        cursor++;
                    }
                }
                else
                {
                    while (cursor < tagContentEnd && !char.IsWhiteSpace(tag[cursor]) && tag[cursor] != '>')
                    {
                        cursor++;
                    }

                    valueEnd = cursor;
                }

                string attributeValue = tag.Substring(valueStart, valueEnd - valueStart);

                if (string.Equals(attributeName, "srcset", StringComparison.OrdinalIgnoreCase))
                {
                    imagesBlockedCallBack();
                    continue;
                }

                sanitized.Append(tag, segmentStart, valueTokenStart - segmentStart);
                if (!string.Equals(attributeName, "src", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized.Append(tag, valueTokenStart, cursor - valueTokenStart);
                    continue;
                }

                if (attributeValue == "#" ||
                    (allowLocalImages && attributeValue.StartsWith("https://localmdimages/", StringComparison.OrdinalIgnoreCase)))
                {
                    sanitized.Append(tag, valueTokenStart, cursor - valueTokenStart);
                }
                else if (allowLocalImages &&
                         HTMLParsingExtension.TryGetLocalImageVirtualUrl(attributeValue, extension.FilePath, extension.AllowedBasePath, out string? virtualUrl))
                {
                    AppendAttributeValue(sanitized, virtualUrl, valueQuote);
                }
                else
                {
                    imagesBlockedCallBack();
                    AppendAttributeValue(sanitized, "#", valueQuote);
                }
            }

            sanitized.Append('>');
            return sanitized.ToString();
        }

        private static void AppendAttributeValue(StringBuilder output, string value, char quote)
        {
            char outputQuote = quote == '\0' ? '"' : quote;
            output.Append(outputQuote);
            output.Append(value);
            output.Append(outputQuote);
        }
    }
}
