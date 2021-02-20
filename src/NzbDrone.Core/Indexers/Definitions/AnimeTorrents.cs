using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using FluentValidation;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class AnimeTorrents : HttpIndexerBase<AnimeTorrentsSettings>
    {
        public override string Name => "AnimeTorrents";

        public override string BaseUrl => "https://animetorrents.me/";
        private string LoginUrl => BaseUrl + "login.php";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;
        public override IndexerCapabilities Capabilities => SetCapabilities();

        public AnimeTorrents(IHttpClient httpClient, IIndexerStatusService indexerStatusService, IConfigService configService, Logger logger)
            : base(httpClient, indexerStatusService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new AnimeTorrentsRequestGenerator() { Settings = Settings, Capabilities = Capabilities, BaseUrl = BaseUrl };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new AnimeTorrentsParser(Settings, Capabilities.Categories, BaseUrl);
        }

        protected override void DoLogin()
        {
            var requestBuilder = new HttpRequestBuilder(LoginUrl)
            {
                LogResponseContent = true
            };

            requestBuilder.Method = HttpMethod.POST;
            requestBuilder.PostProcess += r => r.RequestTimeout = TimeSpan.FromSeconds(15);

            var cookies = Cookies;

            Cookies = null;
            var authLoginRequest = requestBuilder
                .AddFormParameter("username", Settings.Username)
                .AddFormParameter("password", Settings.Password)
                .AddFormParameter("form", "login")
                .AddFormParameter("rememberme[]", "1")
                .SetHeader("Content-Type", "multipart/form-data")
                .Build();

            var response = _httpClient.Execute(authLoginRequest);

            cookies = response.GetCookies();
            UpdateCookies(cookies, DateTime.Now + TimeSpan.FromDays(30));

            _logger.Debug("AnimeTorrents authentication succeeded.");
        }

        protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
        {
            if (httpResponse.Content.Equals("Access Denied!"))
            {
                return true;
            }

            return false;
        }

        private IndexerCapabilities SetCapabilities()
        {
            var caps = new IndexerCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                       {
                           TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep
                       },
                MovieSearchParams = new List<MovieSearchParam>
                       {
                           MovieSearchParam.Q
                       }
            };

            caps.Categories.AddCategoryMapping(1, NewznabStandardCategory.MoviesSD, "Anime Movie");
            caps.Categories.AddCategoryMapping(6, NewznabStandardCategory.MoviesHD, "Anime Movie HD");
            caps.Categories.AddCategoryMapping(2, NewznabStandardCategory.TVAnime, "Anime Series");
            caps.Categories.AddCategoryMapping(7, NewznabStandardCategory.TVAnime, "Anime Series HD");
            caps.Categories.AddCategoryMapping(5, NewznabStandardCategory.XXXDVD, "Hentai (censored)");
            caps.Categories.AddCategoryMapping(9, NewznabStandardCategory.XXXDVD, "Hentai (censored) HD");
            caps.Categories.AddCategoryMapping(4, NewznabStandardCategory.XXXDVD, "Hentai (un-censored)");
            caps.Categories.AddCategoryMapping(8, NewznabStandardCategory.XXXDVD, "Hentai (un-censored) HD");
            caps.Categories.AddCategoryMapping(13, NewznabStandardCategory.BooksForeign, "Light Novel");
            caps.Categories.AddCategoryMapping(3, NewznabStandardCategory.BooksComics, "Manga");
            caps.Categories.AddCategoryMapping(10, NewznabStandardCategory.BooksComics, "Manga 18+");
            caps.Categories.AddCategoryMapping(11, NewznabStandardCategory.TVAnime, "OVA");
            caps.Categories.AddCategoryMapping(12, NewznabStandardCategory.TVAnime, "OVA HD");
            caps.Categories.AddCategoryMapping(14, NewznabStandardCategory.BooksComics, "Doujin Anime");
            caps.Categories.AddCategoryMapping(15, NewznabStandardCategory.XXXDVD, "Doujin Anime 18+");
            caps.Categories.AddCategoryMapping(16, NewznabStandardCategory.AudioForeign, "Doujin Music");
            caps.Categories.AddCategoryMapping(17, NewznabStandardCategory.BooksComics, "Doujinshi");
            caps.Categories.AddCategoryMapping(18, NewznabStandardCategory.BooksComics, "Doujinshi 18+");
            caps.Categories.AddCategoryMapping(19, NewznabStandardCategory.Audio, "OST");

            return caps;
        }
    }

    public class AnimeTorrentsRequestGenerator : IIndexerRequestGenerator
    {
        public AnimeTorrentsSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }
        public string BaseUrl { get; set; }

        public AnimeTorrentsRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories)
        {
            var searchString = term;

            //  replace any space, special char, etc. with % (wildcard)
            var replaceRegex = new Regex("[^a-zA-Z0-9]+");
            searchString = replaceRegex.Replace(searchString, "%");
            var searchUrl = BaseUrl + "ajax/torrents_data.php";

            var queryCollection = new NameValueCollection
            {
                { "total", "146" }, // Not sure what this is about but its required!
                { "cat", Capabilities.Categories.MapTorznabCapsToTrackers(categories).SingleOrDefault() ?? "0" },
                { "page", "1" },
                { "searchin", "filename" },
                { "search", searchString }
            };

            searchUrl += "?" + queryCollection.GetQueryString();

            var extraHeaders = new NameValueCollection
            {
                { "X-Requested-With", "XMLHttpRequest" }
            };

            var request = new IndexerRequest(searchUrl, HttpAccept.Rss);
            request.HttpRequest.Headers.Add(extraHeaders);

            yield return request;
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class AnimeTorrentsParser : IParseIndexerResponse
    {
        private readonly AnimeTorrentsSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;
        private readonly string _baseUrl;

        public AnimeTorrentsParser(AnimeTorrentsSettings settings, IndexerCapabilitiesCategories categories, string baseUrl)
        {
            _settings = settings;
            _categories = categories;
            _baseUrl = baseUrl;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<TorrentInfo>();

            var parser = new HtmlParser();
            var dom = parser.ParseDocument(indexerResponse.Content);

            var rows = dom.QuerySelectorAll("tr");
            foreach (var row in rows.Skip(1))
            {
                var release = new TorrentInfo();
                var qTitleLink = row.QuerySelector("td:nth-of-type(2) a:nth-of-type(1)");
                release.Title = qTitleLink.TextContent.Trim();

                // If we search an get no results, we still get a table just with no info.
                if (string.IsNullOrWhiteSpace(release.Title))
                {
                    break;
                }

                release.Guid = qTitleLink.GetAttribute("href");
                release.InfoUrl = release.Guid;

                var dateString = row.QuerySelector("td:nth-of-type(5)").TextContent;
                release.PublishDate = DateTime.ParseExact(dateString, "dd MMM yy", CultureInfo.InvariantCulture);

                // newbie users don't see DL links
                var qLink = row.QuerySelector("td:nth-of-type(3) a");
                if (qLink != null)
                {
                    release.DownloadUrl = qLink.GetAttribute("href");
                }
                else
                {
                    // use details link as placeholder
                    // null causes errors during export to torznab
                    // skipping the release prevents newbie users from adding the tracker (empty result)
                    release.DownloadUrl = release.InfoUrl;
                }

                var sizeStr = row.QuerySelector("td:nth-of-type(6)").TextContent;
                release.Size = ReleaseInfo.GetBytes(sizeStr);

                var connections = row.QuerySelector("td:nth-of-type(8)").TextContent.Trim().Split("/".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

                release.Seeders = ParseUtil.CoerceInt(connections[0].Trim());
                release.Peers = ParseUtil.CoerceInt(connections[1].Trim()) + release.Seeders;
                release.Grabs = ParseUtil.CoerceInt(connections[2].Trim());

                var rCat = row.QuerySelector("td:nth-of-type(1) a").GetAttribute("href");
                var rCatIdx = rCat.IndexOf("cat=");
                if (rCatIdx > -1)
                {
                    rCat = rCat.Substring(rCatIdx + 4);
                }

                release.Category = _categories.MapTrackerCatToNewznab(rCat);

                if (row.QuerySelector("img[alt=\"Gold Torrent\"]") != null)
                {
                    release.DownloadVolumeFactor = 0;
                }
                else if (row.QuerySelector("img[alt=\"Silver Torrent\"]") != null)
                {
                    release.DownloadVolumeFactor = 0.5;
                }
                else
                {
                    release.DownloadVolumeFactor = 1;
                }

                var uLFactorImg = row.QuerySelector("img[alt*=\"x Multiplier Torrent\"]");
                if (uLFactorImg != null)
                {
                    release.UploadVolumeFactor = ParseUtil.CoerceDouble(uLFactorImg.GetAttribute("alt").Split('x')[0]);
                }
                else
                {
                    release.UploadVolumeFactor = 1;
                }

                qTitleLink.Remove();

                //release.Description = row.QuerySelector("td:nth-of-type(2)").TextContent;
                torrentInfos.Add(release);
            }

            return torrentInfos.ToArray();
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class AnimeTorrentsSettingsValidator : AbstractValidator<AnimeTorrentsSettings>
    {
        public AnimeTorrentsSettingsValidator()
        {
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();
        }
    }

    public class AnimeTorrentsSettings : IProviderConfig
    {
        private static readonly AnimeTorrentsSettingsValidator Validator = new AnimeTorrentsSettingsValidator();

        public AnimeTorrentsSettings()
        {
            Username = "";
            Password = "";
        }

        [FieldDefinition(1, Label = "Username", HelpText = "Site username")]
        public string Username { get; set; }

        [FieldDefinition(2, Label = "Password", Type = FieldType.Password, HelpText = "Site password", Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(3, Label = "FreeLeech Only", Type = FieldType.Checkbox, Advanced = true, HelpText = "Search Freeleech torrents only")]
        public bool FreeLeechOnly { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
