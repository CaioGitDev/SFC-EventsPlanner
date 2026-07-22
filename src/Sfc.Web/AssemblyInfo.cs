using System.Runtime.CompilerServices;

// Lets Sfc.Web.Tests call internal members directly — currently only
// SeedImporter.EnsureTransitionSucceeded, whose failure branch is unreachable through the
// public ImportAsync surface (see its XML doc comment) and can only be unit-tested this way.
[assembly: InternalsVisibleTo("Sfc.Web.Tests")]
