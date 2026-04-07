using System.Collections;
using Newtonsoft.Json;

namespace Alife.Framework;

[JsonObject]
public class StringFolder : IEnumerable<string>, IComparable<StringFolder>
{
    public string Name { get; set; }
    public SortedSet<string> Strings { get; }
    public SortedSet<StringFolder> Folders { get; }

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
        Strings.RemoveWhere(predicate);
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
        Strings = new SortedSet<string>();
        Folders = new SortedSet<StringFolder>();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int CompareTo(StringFolder? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;
        return string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}
