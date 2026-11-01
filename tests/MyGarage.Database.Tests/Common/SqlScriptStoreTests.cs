using MyGarage.Database.Common;

namespace MyGarage.Database.Tests.Common;

public class SqlScriptStoreTests
{
    [Fact]
    public void Get_ReturnsScriptText()
    {
        var text = new SqlScriptStore().Get("Migrations/0001_create_vehicles");

        Assert.Contains("CREATE TABLE vehicles", text);
    }

    [Fact]
    public void Get_CachesScriptText()
    {
        var store = new SqlScriptStore();

        Assert.Same(store.Get("Common/GetAppliedMigrations"), store.Get("Common/GetAppliedMigrations"));
    }

    [Fact]
    public void Get_UnknownScript_ThrowsNamingTheScript()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new SqlScriptStore().Get("Vehicles/DoesNotExist"));

        Assert.Contains("Vehicles/DoesNotExist", exception.Message);
    }

    [Fact]
    public void EveryEmbeddedScriptResource_IsASqlFile()
    {
        var resources = SqlScriptStore.AllResourceNames();

        Assert.NotEmpty(resources);
        Assert.All(resources, resource => Assert.EndsWith(".sql", resource, StringComparison.Ordinal));
    }

    [Fact]
    public void List_ReturnsMigrationsInOrder()
    {
        var migrations = new SqlScriptStore().List("Migrations");

        Assert.Equal(
            ["0001_create_vehicles", "0002_create_vehicle_tags", "0003_create_journal_notes", "0004_create_vehicle_images"],
            migrations);
    }
}
