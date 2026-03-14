using System.Collections;
using Newtonsoft.Json;

[JsonObject]
public class StringFolder : IEnumerable<string>
{
    public string Name { get; set; }
    public List<string> Strings { get; }
    public List<StringFolder> Folders { get; }

    public IEnumerator<string> GetEnumerator()
    {
        foreach (string plugin in Strings)
            yield return plugin;
        foreach (StringFolder folder in Folders)
        {
            foreach (string folderPlugin in folder)
                yield return folderPlugin;
        }
    }
    public void RemoveAll(Predicate<string> predicate)
    {
        foreach (StringFolder folder in Folders)
            folder.RemoveAll(predicate);
        Strings.RemoveAll(predicate);
    }
    public bool Remove(string str)
    {
        if (Strings.Remove(str))
            return true;

        foreach (StringFolder folder in Folders)
        {
            if (folder.Remove(str))
                return true;
        }

        return false;
    }

    public StringFolder(string name)
    {
        Name = name;
        Strings = new List<string>();
        Folders = new List<StringFolder>();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
