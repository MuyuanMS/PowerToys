// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using ManagedCommon;

namespace AdvancedPaste.Helpers;

internal static class RichTextHelper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static string ToRichText(string markdown)
    {
        Logger.LogTrace();

        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var document = Markdown.Parse(markdown, Pipeline);
        SanitizeLinks(document);
        return Markdown.ToHtml(document, Pipeline);
    }

    private static void SanitizeLinks(ContainerBlock container)
    {
        foreach (var block in container)
        {
            if (block is ContainerBlock childContainer)
            {
                SanitizeLinks(childContainer);
            }

            if (block is LeafBlock { Inline: { } inline })
            {
                SanitizeLinks(inline);
            }
        }
    }

    private static void SanitizeLinks(ContainerInline container)
    {
        for (var inline = container.FirstChild; inline is not null;)
        {
            var nextInline = inline.NextSibling;
            if (inline is LinkInline link)
            {
                SanitizeLinks(link);
                if (link.IsImage || !IsSafeLink(link))
                {
                    for (var child = link.FirstChild; child is not null;)
                    {
                        var nextChild = child.NextSibling;
                        child.Remove();
                        link.InsertBefore(child);
                        child = nextChild;
                    }

                    link.Remove();
                }
            }
            else if (inline is AutolinkInline autolink && !IsSafeAutolink(autolink))
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
            }
            else if (inline is ContainerInline childContainer)
            {
                SanitizeLinks(childContainer);
            }

            inline = nextInline;
        }
    }

    private static bool IsSafeLink(LinkInline link)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
        if (IsSafeUri(url))
        {
            link.Url = url;
            link.GetDynamicUrl = null;
            return true;
        }

        return false;
    }

    private static bool IsSafeAutolink(AutolinkInline link)
    {
        var url = link.IsEmail ? $"mailto:{link.Url}" : link.Url;
        return IsSafeUri(url);
    }

    private static bool IsSafeUri(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase));
    }
}
