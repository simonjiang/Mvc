// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNet.FileProviders;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Mvc.TagHelpers.Internal;
using Microsoft.AspNet.Razor.Runtime.TagHelpers;
using Microsoft.AspNet.WebUtilities;
using Microsoft.Framework.Caching.Memory;
using Microsoft.Framework.Logging;
using Microsoft.Framework.WebEncoders;

namespace Microsoft.AspNet.Mvc.TagHelpers
{
    /// <summary>
    /// <see cref="ITagHelper"/> implementation targeting &lt;link&gt; elements that supports fallback href paths.
    /// </summary>
    public class LinkTagHelper : TagHelper
    {
        private const string HrefIncludeAttributeName = "asp-href-include";
        private const string HrefExcludeAttributeName = "asp-href-exclude";
        private const string FallbackHrefAttributeName = "asp-fallback-href";
        private const string FallbackHrefIncludeAttributeName = "asp-fallback-href-include";
        private const string FallbackHrefExcludeAttributeName = "asp-fallback-href-exclude";
        private const string FallbackTestClassAttributeName = "asp-fallback-test-class";
        private const string FallbackTestPropertyAttributeName = "asp-fallback-test-property";
        private const string FallbackTestValueAttributeName = "asp-fallback-test-value";
        private const string FallbackJavaScriptResourceName = "compiler/resources/LinkTagHelper_FallbackJavaScript.js";
        private const string FileVersionAttributeName = "asp-file-version";

        private static readonly ModeAttributes<Mode>[] ModeDetails = new[] {
            // Regular src with file version alone
            ModeAttributes.Create(Mode.FileVersion, new[] { FileVersionAttributeName }),
            // Globbed Href (include only) no static href
            ModeAttributes.Create(Mode.GlobbedHref, new [] { HrefIncludeAttributeName }),
            // Globbed Href (include & exclude), no static href
            ModeAttributes.Create(Mode.GlobbedHref, new [] { HrefIncludeAttributeName, HrefExcludeAttributeName }),
            // Fallback with static href
            ModeAttributes.Create(
                Mode.Fallback, new[]
                {
                    FallbackHrefAttributeName,
                    FallbackTestClassAttributeName,
                    FallbackTestPropertyAttributeName,
                    FallbackTestValueAttributeName
                }),
            // Fallback with globbed href (include only)
            ModeAttributes.Create(
                Mode.Fallback, new[] {
                    FallbackHrefIncludeAttributeName,
                    FallbackTestClassAttributeName,
                    FallbackTestPropertyAttributeName,
                    FallbackTestValueAttributeName
                }),
            // Fallback with globbed href (include & exclude)
            ModeAttributes.Create(
                Mode.Fallback, new[] {
                    FallbackHrefIncludeAttributeName,
                    FallbackHrefExcludeAttributeName,
                    FallbackTestClassAttributeName,
                    FallbackTestPropertyAttributeName,
                    FallbackTestValueAttributeName
                }),
        };

        private enum Mode
        {
            /// <summary>
            /// Just adding a file version for the src.
            /// </summary>
            FileVersion = 0,
            /// <summary>
            /// Just performing file globbing search for the href, rendering a separate &lt;link&gt; for each match.
            /// </summary>
            GlobbedHref = 1,
            /// <summary>
            /// Rendering a fallback block if primary stylesheet fails to load. Will also do globbing for both the
            /// primary and fallback hrefs if the appropriate properties are set.
            /// </summary>
            Fallback = 2,
        }

        /// <summary>
        /// A comma separated list of globbed file patterns of CSS stylesheets to load.
        /// The glob patterns are assessed relative to the application's 'webroot' setting.
        /// </summary>
        [HtmlAttributeName(HrefIncludeAttributeName)]
        public string HrefInclude { get; set; }

        /// <summary>
        /// A comma separated list of globbed file patterns of CSS stylesheets to exclude from loading.
        /// The glob patterns are assessed relative to the application's 'webroot' setting.
        /// Must be used in conjunction with <see cref="HrefInclude"/>.
        /// </summary>
        [HtmlAttributeName(HrefExcludeAttributeName)]
        public string HrefExclude { get; set; }

        /// <summary>
        /// The URL of a CSS stylesheet to fallback to in the case the primary one fails.
        /// </summary>
        [HtmlAttributeName(FallbackHrefAttributeName)]
        public string FallbackHref { get; set; }

        /// <summary>
        /// Value indicating if file version should be appended to the href urls.
        /// </summary>
        [HtmlAttributeName(FileVersionAttributeName)]
        public string FileVersion { get; set; }

        /// <summary>
        /// A comma separated list of globbed file patterns of CSS stylesheets to fallback to in the case the primary
        /// one fails.
        /// The glob patterns are assessed relative to the application's 'webroot' setting.
        /// </summary>
        [HtmlAttributeName(FallbackHrefIncludeAttributeName)]
        public string FallbackHrefInclude { get; set; }

        /// <summary>
        /// A comma separated list of globbed file patterns of CSS stylesheets to exclude from the fallback list, in
        /// the case the primary one fails.
        /// The glob patterns are assessed relative to the application's 'webroot' setting.
        /// Must be used in conjunction with <see cref="FallbackHrefInclude"/>.
        /// </summary>
        [HtmlAttributeName(FallbackHrefExcludeAttributeName)]
        public string FallbackHrefExclude { get; set; }

        /// <summary>
        /// The class name defined in the stylesheet to use for the fallback test.
        /// Must be used in conjunction with <see cref="FallbackTestProperty"/> and <see cref="FallbackTestValue"/>,
        /// and either <see cref="FallbackHref"/> or <see cref="FallbackHrefInclude"/>.
        /// </summary>
        [HtmlAttributeName(FallbackTestClassAttributeName)]
        public string FallbackTestClass { get; set; }

        /// <summary>
        /// The CSS property name to use for the fallback test.
        /// Must be used in conjunction with <see cref="FallbackTestClass"/> and <see cref="FallbackTestValue"/>,
        /// and either <see cref="FallbackHref"/> or <see cref="FallbackHrefInclude"/>.
        /// </summary>
        [HtmlAttributeName(FallbackTestPropertyAttributeName)]
        public string FallbackTestProperty { get; set; }

        /// <summary>
        /// The CSS property value to use for the fallback test.
        /// Must be used in conjunction with <see cref="FallbackTestClass"/> and <see cref="FallbackTestProperty"/>,
        /// and either <see cref="FallbackHref"/> or <see cref="FallbackHrefInclude"/>.
        /// </summary>
        [HtmlAttributeName(FallbackTestValueAttributeName)]
        public string FallbackTestValue { get; set; }

        // Properties are protected to ensure subclasses are correctly activated.
        // Internal for ease of use when testing.
        [Activate]
        protected internal ILoggerFactory LoggerFactory { get; set; }

        // TODO: will remove LoggerFactory and activate logger once DI/hosting bug is fixed
        internal ILogger<LinkTagHelper> Logger { get; set; }

        [Activate]
        protected internal IHostingEnvironment HostingEnvironment { get; set; }

        [Activate]
        protected internal ViewContext ViewContext { get; set; }

        [Activate]
        protected internal IMemoryCache Cache { get; set; }

        [Activate]
        protected internal IHtmlEncoder HtmlEncoder { get; set; }

        // Internal for ease of use when testing.
        protected internal GlobbingUrlBuilder GlobbingUrlBuilder { get; set; }

        /// <inheritdoc />
        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var modeResult = AttributeMatcher.DetermineMode(context, ModeDetails);

            var logger = Logger ?? LoggerFactory.CreateLogger<ScriptTagHelper>();

            modeResult.LogDetails(logger, this, context.UniqueId, ViewContext.View.Path);

            if (!modeResult.FullMatches.Any())
            {
                // No attributes matched so we have nothing to do
                return;
            }

            // Get the highest matched mode
            var mode = modeResult.FullMatches.Select(match => match.Mode).Max();

            // NOTE: Values in TagHelperOutput.Attributes are already HtmlEncoded
            var attributes = new Dictionary<string, string>(output.Attributes);

            var builder = new DefaultTagHelperContent();

            if (mode == Mode.FileVersion)
            {
                BuildLinkTag(attributes, builder);
            }
            else if (mode == Mode.Fallback && string.IsNullOrEmpty(HrefInclude))
            {
                // No globbing to do, just build a <link /> tag to match the original one in the source file
                BuildLinkTag(attributes, builder);
            }
            else
            {
                BuildGlobbedLinkTags(attributes, builder);
            }

            if (mode == Mode.Fallback)
            {
                BuildFallbackBlock(builder);
            }

            // We've taken over tag rendering, so prevent rendering the outer tag
            output.TagName = null;
            output.Content.SetContent(builder);
        }

        private void BuildGlobbedLinkTags(IDictionary<string, string> attributes, TagHelperContent builder)
        {
            // Build a <link /> tag for each matched href as well as the original one in the source file
            string staticHref;
            attributes.TryGetValue("href", out staticHref);

            EnsureGlobbingUrlBuilder();
            var urls = GlobbingUrlBuilder.BuildUrlList(staticHref, HrefInclude, HrefExclude);

            foreach (var url in urls)
            {
                attributes["href"] = url;
                BuildLinkTag(attributes, builder);
            }
        }

        private void BuildFallbackBlock(TagHelperContent builder)
        {
            EnsureGlobbingUrlBuilder();
            var fallbackHrefs =
                GlobbingUrlBuilder.BuildUrlList(FallbackHref, FallbackHrefInclude, FallbackHrefExclude).ToArray();

            if (fallbackHrefs.Any())
            {
                if (FileVersion == "true")
                {
                    for (var i=0; i < fallbackHrefs.Count(); i++)
                    {
                        fallbackHrefs[i] += "?v=" + GetFileContentEncodedString(fallbackHrefs[i]);
                    }
                }

                builder.Append(Environment.NewLine);

                // Build the <meta /> tag that's used to test for the presence of the stylesheet
                builder.Append(string.Format(
                    CultureInfo.InvariantCulture,
                    "<meta name=\"x-stylesheet-fallback-test\" class=\"{0}\" />",
                    HtmlEncoder.HtmlEncode(FallbackTestClass)));
                
                // Build the <script /> tag that checks the effective style of <meta /> tag above and renders the extra
                // <link /> tag to load the fallback stylesheet if the test CSS property value is found to be false,
                // indicating that the primary stylesheet failed to load.
                builder.Append("<script>")
                       .Append(string.Format(CultureInfo.InvariantCulture,
                            JavaScriptResources.GetEmbeddedJavaScript(FallbackJavaScriptResourceName),
                            JavaScriptStringEncoder.Default.JavaScriptStringEncode(FallbackTestProperty),
                            JavaScriptStringEncoder.Default.JavaScriptStringEncode(FallbackTestValue),
                            JavaScriptStringArrayEncoder.Encode(JavaScriptStringEncoder.Default, fallbackHrefs)))
                       .Append("</script>");
            }
        }

        private void EnsureGlobbingUrlBuilder()
        {
            if (GlobbingUrlBuilder == null)
            {
                GlobbingUrlBuilder = new GlobbingUrlBuilder(
                    HostingEnvironment.WebRootFileProvider,
                    Cache,
                    ViewContext.HttpContext.Request.PathBase);
            }
        }

        private void BuildLinkTag(IDictionary<string, string> attributes, TagHelperContent builder)
        {
            builder.Append("<link ");

            foreach (var attribute in attributes)
            {
                if (attribute.Key == "href")
                {
                    AppendHref(builder, attribute.Key, attribute.Value);
                }
                else
                {
                    builder.Append(
                        string.Format(CultureInfo.InvariantCulture, "{0}=\"{1}\" ", attribute.Key, attribute.Value));
                }
            }

            builder.Append("/>");
        }

        private void AppendHref(TagHelperContent content, string hrefKey, string hrefValue)
        {
            string formatter = "{0}=\"{1}";
            string fileVersion = string.Empty;
            if (FileVersion == "true")
            {
                fileVersion = GetFileContentEncodedString(hrefValue);
                // This is the case if there was any exception thrown when reading the file.
                if (fileVersion != string.Empty)
                {
                    formatter += "?v={2}";
                }
            }

            formatter += "\" ";

            content.Append(
                string.Format(
                    CultureInfo.InvariantCulture,
                    formatter,
                    hrefKey,
                    HtmlEncoder.HtmlEncode(hrefValue),
                    fileVersion));
        }

        private string GetFileContentEncodedString(string filePath)
        {
            var fileContents = ReadFileContents(HostingEnvironment.WebRootFileProvider.GetFileInfo(filePath));
            if (fileContents != null)
            {
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(System.Text.Encoding.Unicode.GetBytes(fileContents));
                    return WebEncoders.Base64UrlEncode(hash);
                }
            }

            return string.Empty;
        }

        private string ReadFileContents(IFileInfo fileInfo)
        {
            try
            {
                using (var reader = new StreamReader(fileInfo.CreateReadStream()))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                // Ignore any failures
                return null;
            }
        }
    }
}