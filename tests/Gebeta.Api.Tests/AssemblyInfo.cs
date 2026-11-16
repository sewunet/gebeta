// One PostgreSQL container and one API server for every test class (xUnit v3 assembly fixture). Each test creates
// its own company with its own TIN and abbreviation, so tests share the database without seeing each other's data.
[assembly: AssemblyFixture(typeof(Gebeta.Api.Tests.GebetaApiFactory))]
