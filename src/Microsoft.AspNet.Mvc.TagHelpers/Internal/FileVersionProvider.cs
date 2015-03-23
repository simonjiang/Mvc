// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using Microsoft.AspNet.FileProviders;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.WebUtilities;
using Microsoft.Framework.Caching.Memory;
using Microsoft.Framework.Runtime;

namespace Microsoft.AspNet.Mvc.TagHelpers.Internal
{
    /// <summary>
    /// Provides version hash for a specified file.
    /// </summary>
    public class FileVersionProvider
    {
        private const string versionKey = "v";

        /// <summary>
        /// Creates a new instance of <see cref="FileVersionProvider"/>.
        /// </summary>
        /// <param name="fileProvider">The file provider to get and watch files.</param>
        /// <param name="applicationName">Name of the applicaiton.</param>
        /// <param name="cache">Cache where versioned urls of files are cached.</param>
        public FileVersionProvider(
            IFileProvider fileProvider,
            string applicationName,
            IMemoryCache cache)
        {
            FileProvider = fileProvider;
            ApplicationName = applicationName;
            Cache = cache;
        }

        /// <summary>
        /// The <see cref="IFileProvider"/> to get and watch files.
        /// </summary>
        public IFileProvider FileProvider { get; }

        /// <summary>
        /// The name of the application.
        /// </summary>
        public string ApplicationName { get; }

        /// <summary>
        /// The <see cref="IMemoryCache"/> to cache the versioned url of files.
        /// </summary>
        public IMemoryCache Cache { get; }

        /// <summary>
        /// Adds version query parameter to the specified file path.
        /// </summary>
        /// <param name="filePath">The path of the file to which version should be added.</param>
        /// <returns>Path containing the version query string.</returns>
        /// <remarks>
        /// The version query string is appended as with the key "v".
        /// </remarks>
        public string AddVersionToFilePath(string filePath)
        {
            var fileInfo = FileProvider.GetFileInfo(filePath);
            if (!fileInfo.Exists)
            {
                if (filePath.Contains(ApplicationName))
                {
                    fileInfo = FileProvider.GetFileInfo(filePath.Split(
                        new string[] { ApplicationName }, StringSplitOptions.None)[1]);
                }

                if (!fileInfo.Exists)
                {
                    // if the file is not in the current server.
                    return filePath;
                }
            }

            if (Cache != null)
            {
                return Cache.GetOrSet(filePath, cacheGetOrSetContext =>
                {
                    var trigger = FileProvider.Watch(filePath);
                    cacheGetOrSetContext.AddExpirationTrigger(trigger);

                    return Cache.Set(
                        filePath,
                        QueryHelpers.AddQueryString(filePath, versionKey, GetHashForFile(fileInfo, filePath)));
                });
            }

            return QueryHelpers.AddQueryString(filePath, versionKey, GetHashForFile(fileInfo, filePath));
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