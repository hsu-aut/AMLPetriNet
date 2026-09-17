using Xunit;

// Aml.Engine keeps static caches (XDocumentWrapper.CheckReferences enumerates a
// shared dictionary) that race when several CAEXDocument instances are built in
// parallel: ids come back reassigned and "Collection was modified" surfaces from
// engine internals. Running the tests one after another avoids that.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
