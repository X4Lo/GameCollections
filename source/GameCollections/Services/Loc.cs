using Playnite.SDK;
using System.Globalization;

namespace GameCollections.Services
{
    /// <summary>
    /// Shortcuts for reading localized strings from Playnite's resource provider.
    /// </summary>
    public static class Loc
    {
        public static string Get(string key)
        {
            return ResourceProvider.GetString(key);
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }

        public static string GameCount(int count)
        {
            return count == 1
                ? Get("LOCGameCollections_GameCountSingle")
                : Format("LOCGameCollections_GameCountPlural", count);
        }

        public static string CollectionCount(int count)
        {
            return count == 1
                ? Get("LOCGameCollections_CollectionCountSingle")
                : Format("LOCGameCollections_CollectionCountPlural", count);
        }

        public static string Rating(double rating)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0:0.#}/10", rating);
        }
    }
}
