namespace Events.Mongo.Options;

internal class MongoOptions
{
    public string ConnectionString { get; set; } = null!;
    public string Database { get; set; } = null!;
}