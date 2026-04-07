namespace Alife.Framework;

public class ChatActivitySystem
{
    public IEnumerable<ChatActivity> GetAllChatActivities()
    {
        return activities.Values;
    }
    public bool IsActivated(Character character)
    {
        return activities.ContainsKey(character.ID);
    }
    public async Task Play(Character character, IProgress<(string, float)>? progress = null)
    {
        ChatActivity chatActivity = await ChatActivity.Create(character, configuration, progress, [
            configuration,
            storageSystem
        ]);
        activities.Add(character.ID, chatActivity);
    }
    public async Task Stop(Character character)
    {
        ChatActivity chatActivity = activities[character.ID];
        await chatActivity.DisposeAsync();
        activities.Remove(character.ID);
    }
    public ChatActivitySystem(ConfigurationSystem configuration, StorageSystem storageSystem)
    {
        this.configuration = configuration;
        this.storageSystem = storageSystem;
    }

    readonly ConfigurationSystem configuration;
    readonly StorageSystem storageSystem;
    readonly Dictionary<string, ChatActivity> activities = new();
}
