using Testcontainers.MsSql;
using Microsoft.Data.SqlClient;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer? _container;
    private readonly string? _localConnectionString;
    private readonly string? _localDatabaseName;

    public DatabaseFixture()
    {
        var localServer = Environment.GetEnvironmentVariable("ELP_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(localServer))
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            return;
        }

        // Mỗi fixture có database riêng, không bao giờ dùng database do người dùng chỉ định.
        _localDatabaseName = $"EnglishLearningPlatformTests_{Guid.NewGuid():N}";
        var connection = new SqlConnectionStringBuilder(localServer)
        {
            InitialCatalog = _localDatabaseName
        };
        _localConnectionString = connection.ConnectionString;
    }

    public string ConnectionString => _localConnectionString ?? _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (_container is not null)
            await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        // Chỉ xóa database có tên ngẫu nhiên do chính fixture này tạo ra.
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(_localConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{_localDatabaseName}') IS NOT NULL " +
            $"BEGIN ALTER DATABASE [{_localDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{_localDatabaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }
}
