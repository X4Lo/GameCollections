using GameCollections.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace GameCollections.Services
{
    /// <summary>
    /// Decides when dynamic collections are re-evaluated, keeping the work away from the UI thread
    /// and from Playnite's startup. All members must be called on the UI thread.
    /// </summary>
    public class DynamicCollectionRefresher
    {
        // Library updates and metadata downloads change many games in quick succession.
        private static readonly TimeSpan GameChangeDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan TimeBasedRulesInterval = TimeSpan.FromHours(1);
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly CollectionManager manager;
        private readonly DispatcherTimer gameChangeTimer;
        private readonly DispatcherTimer fullRefreshTimer;
        private readonly DispatcherTimer timeBasedRulesTimer;
        private readonly HashSet<Guid> pendingGames = new HashSet<Guid>();
        private bool isFullRefreshRunning;
        private bool isFullRefreshRequested;

        public DynamicCollectionRefresher(IPlayniteAPI playniteApi, CollectionManager manager, Dispatcher dispatcher)
        {
            this.playniteApi = playniteApi;
            this.manager = manager;

            gameChangeTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = GameChangeDelay };
            gameChangeTimer.Tick += (s, e) =>
            {
                gameChangeTimer.Stop();
                ProcessPendingGames();
            };

            fullRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            fullRefreshTimer.Tick += async (s, e) =>
            {
                fullRefreshTimer.Stop();
                await RefreshAllAsync();
            };

            timeBasedRulesTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeBasedRulesInterval };
            timeBasedRulesTimer.Tick += async (s, e) =>
            {
                // "Played in the last 30 days" changes with time alone, without any game event to react to.
                if (manager.Collections.Any(c => RuleEngine.DependsOnCurrentTime(c.Rules)))
                {
                    await RefreshAllAsync();
                }
            };
        }

        /// <summary>
        /// Catches up on changes made while Playnite or the plugin wasn't running.
        /// </summary>
        public void Start()
        {
            ScheduleFullRefresh(StartupDelay);
            timeBasedRulesTimer.Start();
        }

        public void Stop()
        {
            gameChangeTimer.Stop();
            fullRefreshTimer.Stop();
            timeBasedRulesTimer.Stop();
        }

        public void NotifyGamesChanged(IEnumerable<Guid> gameIds)
        {
            pendingGames.UnionWith(gameIds);
            gameChangeTimer.Stop();
            gameChangeTimer.Start();
        }

        public void ScheduleFullRefresh(TimeSpan delay)
        {
            fullRefreshTimer.Stop();
            fullRefreshTimer.Interval = delay;
            fullRefreshTimer.Start();
        }

        /// <summary>
        /// Re-checks every dynamic collection against the whole library on a background thread.
        /// </summary>
        public async Task RefreshAllAsync()
        {
            if (isFullRefreshRunning)
            {
                isFullRefreshRequested = true;
                return;
            }

            isFullRefreshRunning = true;
            try
            {
                var targets = manager.Collections
                    .Where(c => c.IsDynamic)
                    .Select(c => new { c.Id, c.Rules })
                    .ToList();

                if (targets.Count > 0)
                {
                    var games = playniteApi.Database.Games.ToList();
                    var now = DateTime.Now;
                    var results = await Task.Run(() => targets
                        .Select(t => new DynamicMatchResult
                        {
                            CollectionId = t.Id,
                            Rules = t.Rules,
                            GameIds = CollectionManager.FindMatches(t.Rules, games, now)
                        })
                        .ToList());

                    manager.ApplyMatches(results);
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to refresh dynamic collections.");
            }
            finally
            {
                isFullRefreshRunning = false;
            }

            // Games changed during the search may be out of date in its results, so they are applied last.
            ProcessPendingGames();
            if (isFullRefreshRequested)
            {
                isFullRefreshRequested = false;
                await RefreshAllAsync();
            }
        }

        private void ProcessPendingGames()
        {
            if (isFullRefreshRunning || pendingGames.Count == 0)
            {
                return;
            }

            var gameIds = pendingGames.ToList();
            pendingGames.Clear();
            try
            {
                manager.ApplyGameChanges(gameIds);
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to update dynamic collections after game changes.");
            }
        }
    }
}
