using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

const string SeedCatalogSampleDataValue = "true";
const string SeedDevelopmentTestUsersValue = "true";

var cache = builder.AddRedis("cache");

//#if (includeSmtp4dev)
var smtp4dev = builder.AddContainer("smtp4dev", "rnwood/smtp4dev")
    .WithHttpEndpoint(targetPort: 80, port: 5080)
    .WithEndpoint(targetPort: 25, scheme: "tcp", name: "smtp")
    .WithHttpHealthCheck("/");

var smtpEndpoint = smtp4dev.GetEndpoint("smtp");
//#endif

//#if (includePgAdmin)
const string PgAdminImageTag = "9.14.0";
const string PgAdminDefaultEmail = "admin@domain.com";
const string PgAdminDefaultPassword = "Happy1..";
//#endif

// PostgreSQL 18 server with a persistent data volume and pgAdmin dashboard.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume();

//#if (includePgAdmin)
postgres.WithPgAdmin(pgAdmin =>
{
    pgAdmin
        .WithImageTag(PgAdminImageTag)
        .WithEnvironment("PGADMIN_DEFAULT_EMAIL", PgAdminDefaultEmail)
        .WithEnvironment("PGADMIN_DEFAULT_PASSWORD", PgAdminDefaultPassword)
        .WithEnvironment("PGADMIN_CONFIG_SERVER_MODE", "False")
        .WithEnvironment("PGADMIN_CONFIG_MASTER_PASSWORD_REQUIRED", "False")
        // Bind directly because pgAdmin's gunicorn responses can trip the Aspire proxy health check.
        .WithHttpEndpoint(targetPort: 80, port: 5050, name: "http", isProxied: false)
        .WaitFor(postgres);

    foreach (var healthCheck in pgAdmin.Resource.Annotations.OfType<HealthCheckAnnotation>().ToArray())
    {
        pgAdmin.Resource.Annotations.Remove(healthCheck);
    }

    pgAdmin.WithHttpHealthCheck("/misc/ping");
}, "pgadmin");
//#endif

// Shared "starter" database consumed by both the API service and the web frontend.
var starterDb = postgres.AddDatabase("starterdb");

var migrations = builder.AddProject<Projects.Starter_MigrationService>("migrations")
    .WithReference(starterDb)
    .WithEnvironment("Catalog__Seed__SampleData", SeedCatalogSampleDataValue)
    .WithEnvironment("Identity__Seed__SeedDevelopmentTestUsers", SeedDevelopmentTestUsersValue)
//#if (includeSmtp4dev)
    .WithEnvironment("Starter__Email__SmtpHost", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Host)}"))
    .WithEnvironment("Starter__Email__SmtpPort", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Port)}"))
//#endif
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
//#if (includeSmtp4dev)
    .WithEnvironment("Starter__Email__SmtpHost", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Host)}"))
    .WithEnvironment("Starter__Email__SmtpPort", ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Port)}"))
    .WaitFor(smtp4dev)
//#endif
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(starterDb)
    .WaitFor(starterDb)
    .WaitForCompletion(migrations);

builder.Build().Run();
