namespace CopyFinder.Models;

public enum KeepRule
{
    PreferOriginalName = 0,
    PreferShortestName = 1,
    PreferOldestFile = 2,
    PreferNewestFile = 3,
    PreferFolder = 4,
    PreferHighestResolution = 5
}
