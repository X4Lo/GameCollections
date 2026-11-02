using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameCollections.Services
{
    /// <summary>
    /// One upgrade of the data file, from <see cref="FromVersion"/> to the next version.
    /// </summary>
    public class DataMigrationStep
    {
        public int FromVersion { get; }
        public Action<JObject> Apply { get; }

        public DataMigrationStep(int fromVersion, Action<JObject> apply)
        {
            FromVersion = fromVersion;
            Apply = apply;
        }
    }

    public class DataMigrationResult
    {
        public string Json { get; set; }

        /// <summary>
        /// The version the file had before migrating.
        /// </summary>
        public int FromVersion { get; set; }
        public bool WasMigrated { get; set; }

        /// <summary>
        /// The file was written by a newer version of the extension, which this one can't migrate back from.
        /// </summary>
        public bool IsNewerThanSupported { get; set; }
    }

    /// <summary>
    /// Upgrades collections.json from whichever version it has to the current one, one step at a time, so
    /// a user can jump across several extension updates. Steps work on the raw JSON rather than the model
    /// classes because they must still understand properties the current classes no longer have.
    /// To change the format: increase <see cref="CurrentVersion"/> and add a step for the previous version.
    /// Existing steps must never be changed once released.
    /// </summary>
    public static class CollectionsDataMigrator
    {
        public const int CurrentVersion = 2;
        public const string VersionProperty = "SchemaVersion";

        private static readonly IReadOnlyList<DataMigrationStep> Steps = new[]
        {
            // 1 -> 2 (extension 1.1.0): added GameCollection.Rules, for which a missing value already means
            // a manual collection, so nothing needs converting.
            new DataMigrationStep(1, data => { })
        };

        public static DataMigrationResult Migrate(string json)
        {
            return Migrate(json, CurrentVersion, Steps);
        }

        /// <summary>
        /// Throws if the file isn't valid JSON or a step is missing or fails. The input is not modified.
        /// </summary>
        public static DataMigrationResult Migrate(string json, int targetVersion, IReadOnlyList<DataMigrationStep> steps)
        {
            var data = JObject.Parse(json);
            var fromVersion = ReadVersion(data);
            var result = new DataMigrationResult { Json = json, FromVersion = fromVersion };
            if (fromVersion > targetVersion)
            {
                result.IsNewerThanSupported = true;
                return result;
            }

            if (fromVersion == targetVersion)
            {
                return result;
            }

            for (var version = fromVersion; version < targetVersion; version++)
            {
                var step = steps.SingleOrDefault(s => s.FromVersion == version);
                if (step == null)
                {
                    throw new InvalidOperationException($"No migration exists from data version {version}.");
                }

                step.Apply(data);
            }

            data[VersionProperty] = targetVersion;
            result.Json = data.ToString();
            result.WasMigrated = true;
            return result;
        }

        private static int ReadVersion(JObject data)
        {
            var token = data[VersionProperty];

            // Every released file has a version, but a hand-edited one may not. Migrating it from the start is safe.
            if (token == null)
            {
                return 1;
            }

            if (token.Type != JTokenType.Integer || token.Value<int>() < 1)
            {
                throw new InvalidOperationException($"Invalid data version: {token}.");
            }

            return token.Value<int>();
        }
    }
}
