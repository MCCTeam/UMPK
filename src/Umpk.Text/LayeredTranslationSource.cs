using System.Diagnostics.CodeAnalysis;

namespace Umpk.Text;

/// <summary>Resolves a key by walking a fixed set of layers in order and returning the first hit.</summary>
/// <remarks>
/// <para>The source supports any number of layers with no protocol-specific wiring: the caller decides what a "layer" means (a server resource-pack override, an era overlay, the vanilla base table, ...) and in what order they are tried.</para>
/// <para>Layers are swapped lock-free: <see cref="TryResolve"/> takes one <see cref="Volatile.Read{T}"/> per layer it visits, and <see cref="SetLayer"/> commits with <see cref="Volatile.Write{T}"/>. A resolve call may observe layer 0's OLD source racing a concurrent swap of layer 1's NEW source, but it can never observe a torn read of any single layer: reference assignment is atomic, and Volatile ordering is what guarantees the write is actually visible to a reader on another thread rather than only possibly so.</para>
/// </remarks>
public sealed class LayeredTranslationSource : ITranslationSource
{
    private readonly ITranslationSource[] _layers;

    /// <summary>Creates a source with the given layers, tried in the given order (index 0 first). Zero layers is legal; such a source resolves nothing.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="layers"/>, or one of its elements, is null.</exception>
    public LayeredTranslationSource(params ITranslationSource[] layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var copy = new ITranslationSource[layers.Length];
        for (int i = 0; i < layers.Length; i++)
            copy[i] = layers[i] ?? throw new ArgumentNullException(nameof(layers), $"layer {i} is null");

        _layers = copy;
    }

    /// <summary>The number of layers this source holds.</summary>
    public int LayerCount => _layers.Length;

    /// <summary>The layer at <paramref name="index"/>, read with the same Volatile discipline as <see cref="TryResolve"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside <c>[0, LayerCount)</c>.</exception>
    public ITranslationSource this[int index]
    {
        get
        {
            CheckIndex(index);
            return Volatile.Read(ref _layers[index]);
        }
    }

    /// <summary>Atomically replaces the layer at <paramref name="index"/>. Visible to any <see cref="TryResolve"/> or indexer read that starts after this call returns; a call already in flight may still see the old layer, per the class remarks.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside <c>[0, LayerCount)</c>.</exception>
    public void SetLayer(int index, ITranslationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        CheckIndex(index);
        Volatile.Write(ref _layers[index], source);
    }

    /// <inheritdoc/>
    public bool TryResolve(string key, [NotNullWhen(true)] out string? template)
    {
        ArgumentNullException.ThrowIfNull(key);

        for (int i = 0; i < _layers.Length; i++)
        {
            ITranslationSource layer = Volatile.Read(ref _layers[i]);
            if (layer.TryResolve(key, out template))
                return true;

        }

        template = null;
        return false;
    }

    private void CheckIndex(int index)
    {
        if ((uint)index >= (uint)_layers.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"must be in [0, {_layers.Length})");

    }
}
