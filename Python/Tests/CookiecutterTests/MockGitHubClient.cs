﻿// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CookiecutterTools;
using Microsoft.CookiecutterTools.Model;

namespace CookiecutterTests {
    class MockGitHubClient : IGitHubClient {
        public Dictionary<Tuple<string, string>, string> Descriptions { get; } = new Dictionary<Tuple<string, string>, string>();

        public Task<bool> FileExistsAsync(GitHubRepoSearchItem repo, string filePath) {
            throw new NotImplementedException();
        }

        public Task<GitHubRepoSearchItem> GetRepositoryDetails(string owner, string name) {
            string description;
            if (Descriptions.TryGetValue(Tuple.Create(owner, name), out description)) {
                var item = new GitHubRepoSearchItem();
                item.Description = description;

                return Task.FromResult(item);
            }

            throw new WebException();
        }

        public Task<GitHubRepoSearchResult> SearchRepositoriesAsync(string requestUrl) {
            throw new NotImplementedException();
        }

        public Task<GitHubRepoSearchResult> StartSearchRepositoriesAsync(string[] terms) {
            throw new NotImplementedException();
        }
    }
}
