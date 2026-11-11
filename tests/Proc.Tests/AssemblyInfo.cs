using TUnit.Core;

// xUnit ran these one at a time (parallelizeTestCollections: false), and many of them time child processes or watch
// them exit: keep running them serially rather than let TUnit's default parallelism skew those timings
[assembly: NotInParallel]
