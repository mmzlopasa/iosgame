using System;
using System.Collections.Generic;

namespace Founders.Core
{
    // MARK: - Time

    /// <summary>Injectable time source. All timed systems read time only through this.</summary>
    public interface IGameClock
    {
        /// <summary>Wall-clock seconds since 1970.</summary>
        double Now();
    }

    public sealed class SystemClock : IGameClock
    {
        public double Now() => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }

    /// <summary>Deterministic clock for tests.</summary>
    public sealed class ManualClock : IGameClock
    {
        public double Time;
        public ManualClock(double time = 1_000_000) { Time = time; }
        public double Now() => Time;
        public void Advance(double seconds) { Time += seconds; }
    }

    // MARK: - Feedback

    public enum NoticeKind { Info, Success, Warning, Milestone }

    /// <summary>Something the player should be told about (shown as a toast by the UI).</summary>
    public sealed class GameNotice
    {
        public readonly NoticeKind Kind;
        public readonly string Message;
        public GameNotice(NoticeKind kind, string message) { Kind = kind; Message = message; }
        public override string ToString() => $"{Kind}: {Message}";
    }

    public enum GameErrorKind
    {
        UnknownContent, Placement, Unaffordable, ConstructionQueueFull, NotUpgradeable, BuildingBusy, LockedByTech,
        TooManyWorkers, NoJobSlots, NothingLeft, ResearchAlreadyDone, ResearchInProgress, MissingPrerequisites, NoPendingEvent,
        OptionUnavailable, ExpeditionInProgress, RegionAlreadyExplored, RegionNotAdjacent, MilestoneAlreadyReached,
        RequirementsNotMet, NotReady, Founding,
    }

    /// <summary>Why a command was refused. Messages are player facing.</summary>
    public sealed class GameError : IEquatable<GameError>
    {
        public readonly GameErrorKind Kind;
        public readonly string Message;
        public readonly PlacementIssue Issue;
        public readonly ResourceBundle Missing;

        GameError(GameErrorKind kind, string message, PlacementIssue issue = null, ResourceBundle missing = default)
        {
            Kind = kind; Message = message; Issue = issue; Missing = missing;
        }

        public static GameError UnknownContent(string id) => new GameError(GameErrorKind.UnknownContent, $"Unknown content '{id}'.");
        public static GameError Placement(PlacementIssue issue) => new GameError(GameErrorKind.Placement, issue.Message, issue);
        public static GameError Unaffordable(ResourceBundle missing) =>
            new GameError(GameErrorKind.Unaffordable, $"Not enough resources. Missing: {missing.ShortDescription}", null, missing);
        public static GameError ConstructionQueueFull(int max) =>
            new GameError(GameErrorKind.ConstructionQueueFull, $"Your people can only build {max} things at once.");
        public static readonly GameError NotUpgradeable = new GameError(GameErrorKind.NotUpgradeable, "This building has no upgrade available.");
        public static readonly GameError BuildingBusy = new GameError(GameErrorKind.BuildingBusy, "This building is still under construction.");
        public static GameError LockedByTech(string name) => new GameError(GameErrorKind.LockedByTech, $"Requires the technology {name}.");
        public static GameError TooManyWorkers(int available) => new GameError(GameErrorKind.TooManyWorkers, $"Only {available} idle people available.");
        public static GameError NoJobSlots(int capacity) =>
            new GameError(GameErrorKind.NoJobSlots, $"No room for more workers (capacity {capacity}). Build more workplaces.");
        public static readonly GameError ResearchAlreadyDone = new GameError(GameErrorKind.ResearchAlreadyDone, "Already researched.");
        public static readonly GameError ResearchInProgress = new GameError(GameErrorKind.ResearchInProgress, "Another research project is in progress.");
        public static readonly GameError NothingLeft = new GameError(GameErrorKind.NothingLeft, "Nothing is left to gather here.");

        public static GameError MissingPrerequisites(List<string> names) =>
            new GameError(GameErrorKind.MissingPrerequisites, $"Research first: {string.Join(", ", names)}.");
        public static readonly GameError NoPendingEvent = new GameError(GameErrorKind.NoPendingEvent, "There is no decision waiting.");
        public static GameError OptionUnavailable(string reason) => new GameError(GameErrorKind.OptionUnavailable, reason);
        public static readonly GameError ExpeditionInProgress = new GameError(GameErrorKind.ExpeditionInProgress, "Scouts are already out exploring.");
        public static readonly GameError RegionAlreadyExplored = new GameError(GameErrorKind.RegionAlreadyExplored, "This area is already explored.");
        public static readonly GameError MilestoneAlreadyReached = new GameError(GameErrorKind.MilestoneAlreadyReached, "Milestone already reached.");
        public static GameError RequirementsNotMet(List<string> list) =>
            new GameError(GameErrorKind.RequirementsNotMet, $"Not ready yet: {string.Join(", ", list)}.");

        public static GameError NotReady(string reason) => new GameError(GameErrorKind.NotReady, reason);
        public static GameError Founding(string reason) => new GameError(GameErrorKind.Founding, reason);
        public static readonly GameError RegionNotAdjacent = new GameError(GameErrorKind.RegionNotAdjacent, "Scouts can only reach land next to what you have already explored.");

        public bool Equals(GameError o) => o != null && Kind == o.Kind && Message == o.Message;
        public override bool Equals(object obj) => Equals(obj as GameError);
        public override int GetHashCode() => HashCode.Combine(Kind, Message);
        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>Outcome of a game command.</summary>
    public readonly struct GameResult
    {
        public readonly GameError Error;
        public bool Ok => Error == null;
        GameResult(GameError error) { Error = error; }
        public static readonly GameResult Success = new GameResult(null);
        public static implicit operator GameResult(GameError error) => new GameResult(error);
        public override string ToString() => Ok ? "Success" : $"Failure({Error})";
    }

    /// <summary>Outcome of a game command that returns a value.</summary>
    public readonly struct GameResult<T>
    {
        public readonly T Value;
        public readonly GameError Error;
        public bool Ok => Error == null;
        GameResult(T value, GameError error) { Value = value; Error = error; }
        public static GameResult<T> Success(T value) => new GameResult<T>(value, null);
        public static implicit operator GameResult<T>(GameError error) => new GameResult<T>(default, error);
        public override string ToString() => Ok ? $"Success({Value})" : $"Failure({Error})";
    }
}
