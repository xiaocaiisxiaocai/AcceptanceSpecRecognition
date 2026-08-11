export enum SpecCleanupScanStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  Cancelled = 3,
  Failed = 4
}

export enum SpecCleanupCategory {
  RecommendedCleanup = 1,
  ManualReview = 2,
  Healthy = 3
}

export enum SpecCleanupReason {
  NeverReferenced = 1,
  LongUnused = 2,
  UntrackedHistoricalReferences = 3,
  CurrentVersionNeverReferenced = 4,
  RecentlyChanged = 5,
  RecentlyUsed = 6
}
