namespace HNovaReader.Services.Reader;

internal sealed class BoundedTextWindowCache(int characterCapacity)
{
    private readonly object syncRoot = new();
    private readonly Dictionary<TextWindowCacheKey, LinkedListNode<CacheEntry>> entries = [];
    private readonly LinkedList<CacheEntry> usage = [];
    private int cachedCharacterCount;

    public bool TryGet(TextWindowCacheKey key, out TextReadWindow window)
    {
        lock (syncRoot)
        {
            if (!entries.TryGetValue(key, out var node))
            {
                window = null!;
                return false;
            }

            usage.Remove(node);
            usage.AddFirst(node);
            window = node.Value.Window;
            return true;
        }
    }

    public void Add(TextWindowCacheKey key, TextReadWindow window)
    {
        if (window.Text.Length > characterCapacity)
        {
            return;
        }

        lock (syncRoot)
        {
            if (entries.Remove(key, out var existing))
            {
                usage.Remove(existing);
                cachedCharacterCount -= existing.Value.Window.Text.Length;
            }

            var node = usage.AddFirst(new CacheEntry(key, window));
            entries.Add(key, node);
            cachedCharacterCount += window.Text.Length;

            while (cachedCharacterCount > characterCapacity && usage.Last is { } leastRecent)
            {
                usage.RemoveLast();
                entries.Remove(leastRecent.Value.Key);
                cachedCharacterCount -= leastRecent.Value.Window.Text.Length;
            }
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            entries.Clear();
            usage.Clear();
            cachedCharacterCount = 0;
        }
    }

    private sealed record CacheEntry(TextWindowCacheKey Key, TextReadWindow Window);
}

internal readonly record struct TextWindowCacheKey(long CharacterOffset, int MaximumCharacters);
