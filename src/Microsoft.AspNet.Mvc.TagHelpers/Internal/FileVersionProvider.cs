// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using Microsoft.AspNet.FileProviders;
using Microsoft.AspNet.WebUtilities;
using Microsoft.Framework.Caching.Memory;
using Microsoft.Framework.Internal;

namespace Microsoft.AspNet.Mvc.TagHelpers.Internal
{
    /// <summary>
    /// Provides version hash for a specified file.
    /// </summary>
    public class FileVersionProvider
    {
        private const string VersionKey = "v";
        private readonly IFileProvider _fileProvider;
        private readonly string _applicationName;
        private readonly IMemoryCache _cache;

        /// <summary>
        /// Creates a new instance of <see cref="FileVersionProvider"/>.
        /// </summary>
        /// <param name="fileProvider">The file provider to get and watch files.</param>
        /// <param name="applicationName">Name of the applicaiton.</param>
        /// <param name="cache">Cache where versioned urls of files are cached.</param>
        public FileVersionProvider(
            [NotNull] IFileProvider fileProvider,
            [NotNull] string applicationName,
            IMemoryCache cache)
        {
            _fileProvider = fileProvider;
            _applicationName = applicationName;
            _cache = cache;
        }

        /// <summary>
        /// Adds version query parameter to the specified file path.
        /// </summary>
        /// <param name="path">The path of the file to which version should be added.</param>
        /// <returns>Path containing the version query string.</returns>
        /// <remarks>
        /// The version query string is appended as with the key "v".
        /// </remarks>
        public string AddFileVersionToPath(string path)
        {
            var fileInfo = _fileProvider.GetFileInfo(path);
            if (!fileInfo.Exists)
            {
                if (path.StartsWith("/" + _applicationName) ||
                    path.StartsWith("~/" + _applicationName))
                {
                    fileInfo = _fileProvider.GetFileInfo(path.Split(
                        new string[] { _applicationName }, StringSplitOptions.None)[1]);
                }

                if (!fileInfo.Exists)
                {
                    // if the file is not in the current server.
                    return path;
                }
            }

            if (_cache != null)
            {
                return _cache.GetOrSet(path, cacheGetOrSetContext =>
                {
                    var trigger = _fileProvider.Watch(path);
                    cacheGetOrSetContext.AddExpirationTrigger(trigger);
                    return QueryHelpers.AddQueryString(path, VersionKey, GetHashForFile(fileInfo, path));
                });
            }

            return QueryHelpers.AddQueryString(path, VersionKey, GetHashForFile(fileInfo, path));
        }

        private string GetHashForFile(IFileInfo fileInfo, string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(fileInfo.CreateReadStream());
                return WebEncoders.Base64UrlEncode(hash);
            }
        }
    }
}