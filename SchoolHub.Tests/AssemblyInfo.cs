using Xunit;

// The integration tests share one database: they create and delete rows, and
// assert on counts in some cases. Running collections in parallel would make
// them interfere with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
