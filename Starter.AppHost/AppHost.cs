using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

const string SeedCatalogSampleDataValue = "true";
const string SeedDevelopmentTestUsersValue = "true";

var cache = builder.AddRedis("cache")
    .WithDataVolume();

var smtp4dev = builder.AddContainer("smtp4dev", "rnwood/smtp4dev")
    .WithHttpEndpoint(targetPort: 80)
    .WithEndpoint(targetPort: 25, scheme: "tcp", name: "smtp")
    .WithHttpHealthCheck("/api/messages");

var smtpEndpoint = smtp4dev.GetEndpoint("smtp");

const string PgAdminImageTag = "9.14.0";
var pgAdminEmail = builder.AddParameter("pgadmin-email", value: "admin@domain.com");
var pgAdminPassword = builder.AddParameter("pgadmin-password", secret: true);

// PostgreSQL 18 server with a persistent data volume.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume();

postgres.WithPgAdmin(pgAdmin =>
{
    pgAdmin
        .WithImageTag(PgAdminImageTag)
        .WithEnvironment("PGADMIN_DEFAULT_EMAIL", pgAdminEmail)
        .WithEnvironment("PGADMIN_DEFAULT_PASSWORD", pgAdminPassword)
        .WithEnvironment("PGADMIN_CONFIG_SERVER_MODE", "False")
        .WithEnvironment("PGADMIN_CONFIG_MASTER_PASSWORD_REQUIRED", "False")
        .WithHttpEndpoint(targetPort: 80, name: "http")
        .WaitFor(postgres);

}, "pgadmin");

// Shared "starter" database consumed by both the API service and the web frontend.
var starterDb = postgres.AddDatabase("starterdb");

var migrations = builder.AddProject<Projects.Starter_MigrationService>("migrations")
    .WithReference(starterDb)
    .WithEnvironment("Catalog__Seed__SampleData", SeedCatalogSampleDataValue)
    .WithEnvironment("Identity__Seed__SeedDevelopmentTestUsers", SeedDevelopmentTestUsersValue)
    .WithEnvironment("Starter__Email__SmtpHost", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Host)}"))
    .WithEnvironment("Starter__Email__SmtpPort", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Port)}"))
    .WaitFor(starterDb);

var apiService = builder.AddProject<Projects.Starter_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(starterDb)
    .WaitFor(starterDb)
    .WaitForCompletion(migrations);

builder.AddProject<Projects.Starter_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithEnvironment("Identity__Seed__SeedDevelopmentTestUsers", SeedDevelopmentTestUsersValue)
    .WithEnvironment("Starter__Email__SmtpHost", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Host)}"))
    .WithEnvironment("Starter__Email__SmtpPort", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Port)}"))
    .WaitFor(smtp4dev)
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(starterDb)
    .WaitFor(starterDb)
    .WaitForCompletion(migrations);

builder.Build().Run();
