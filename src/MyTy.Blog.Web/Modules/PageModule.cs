﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Hosting;
using System.Xml.Linq;
using MyTy.Blog.Web.Models;
using MyTy.Blog.Web.Models.Github;
using MyTy.Blog.Web.Services;
using MyTy.Blog.Web.ViewModels;
using Nancy;

namespace MyTy.Blog.Web.Modules
{
    public class PageModule : NancyModule
    {
        readonly string siteBasePath = HostingEnvironment.MapPath(@"~/");
        readonly BlogDB db;
        readonly IApplicationConfiguration config;

        public PageModule(BlogDB db, IApplicationConfiguration config)
        {
            this.db = db;
            this.config = config;

            Get["/{slug}"] = (parameters) => {
                if (parameters.slug == "sitemap") {
                    return Sitemap();
                }

                var fileLocation = String.Format("App_Data\\Content\\Pages\\{0}.md", parameters.slug);

                var page = db.Pages.FirstOrDefault(p => p.FileLocation == fileLocation);

                if (page == null) {
                    return Response.AsError(HttpStatusCode.NotFound);
                }

                return View[page.Layout, new PageDetailViewModel {
                    Page = page
                }];
            };

            Post["/{slug}", true] = async (parameters, ct) => {
                if (parameters.slug == "sitemap") {
                    if (config.CanRefresh(Request)) {
                        await GetLatestContent();
                    }

                    return Sitemap();
                }

                return Response.AsError(HttpStatusCode.ServiceUnavailable);
            };
        }

        private GitHubMirror CreateGitHubMirror(GitHubDirectorySync dirSync, string gitHubToken)
        {
            return new GitHubMirror(dirSync.owner, dirSync.repo, dirSync.branch, dirSync.remotePath, dirSync.locaPath, gitHubToken);
        }

        private async Task GetLatestContent()
        {
            if (!String.IsNullOrWhiteSpace(config.GitHubToken)) {
                var updatePostsTask = (config.PostsSync == null) ? Task.Delay(0) : CreateGitHubMirror(config.PostsSync, config.GitHubToken)
                    .SynchronizeAsync().ContinueWith(t => {
                        var deletePosts = db.Posts
                            .Select(p => p.FileLocation)
                            .Select(f => Path.Combine(siteBasePath, f))
                            .Where(f => !File.Exists(f))
                            .ToArray();

                        var postsUpdater = new PostUpdater(db);
                        foreach (var file in deletePosts) {
                            postsUpdater.FileDeleted(file);
                        }

                        foreach (var file in Directory.EnumerateFiles(config.PostsSync.locaPath, "*", SearchOption.AllDirectories)) {
                            postsUpdater.FileUpdated(file);
                        }
                    });

                var updatePagesTask = (config.PostsSync == null) ? Task.Delay(0) : CreateGitHubMirror(config.PagesSync, config.GitHubToken)
                    .SynchronizeAsync().ContinueWith(t => {
                        var deletePages = db.Pages
                            .Select(p => p.FileLocation)
                            .Select(f => Path.Combine(siteBasePath, f))
                            .Where(f => !File.Exists(f))
                            .ToArray();

                        var pagesUpdater = new PageUpdater(db);
                        foreach (var file in deletePages) {
                            pagesUpdater.FileDeleted(file);
                        }

                        foreach (var file in Directory.EnumerateFiles(config.PagesSync.locaPath, "*", SearchOption.AllDirectories)) {
                            pagesUpdater.FileUpdated(file);
                        }
                    });

                var updateOtherDirsTask = (config.OthersSync == null) ? Task.Delay(0) : Task.WhenAll(
                    config.OthersSync.Select(o =>
                        CreateGitHubMirror(o, config.GitHubToken).SynchronizeAsync(false)));

                await Task.WhenAll(
                    updatePostsTask,
                    updatePagesTask,
                    updateOtherDirsTask
                );
            }
        }

        private dynamic Sitemap()
        {
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var homepage = new XElement[] {
					new XElement(ns + "url",
						new XElement(ns + "loc", config.BaseUrl)
					)
				};

            var pages = db.Pages.Select(p => new XElement(ns + "url",
                new XElement(ns + "loc", config.BaseUrl + p.Href)
            ));

            var posts = db.Posts.Select(p => new XElement(ns + "url",
                new XElement(ns + "loc", config.BaseUrl + p.Href)
            ));

            return Response.AsXml(homepage.Concat(pages).Concat(posts).ToArray());
        }
    }
}
