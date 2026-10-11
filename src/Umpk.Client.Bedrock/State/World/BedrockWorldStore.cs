using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.World;

// The store keeps one entry per chunk column and section with explicit availability so unknown terrain never reads as air.
// Completed assemblies and matched sub-chunk entries commit only when the caller generation still matches the store generation.
// Counts, bytes, columns, sections, and block actors stay bounded; expiry happens through explicit invalidation, never a timer.
internal sealed class BedrockWorldStore
{
    internal const int MaxColumns = 256;
    internal const int MaxSectionsPerColumn = 64;
    internal const int MaxSectionsTotal = 2048;
    internal const long MaxRetainedBytes = 16L * 1024 * 1024;
    internal const int MaxBlockActors = 1024;
    internal const int MaxSinglePayloadBytes = 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly Dictionary<ColumnKey, Column> _columns = new();
    private readonly Dictionary<BlockActorKey, byte[]> _blockActors = new();
    private int _worldGeneration = 1;
    private long _requestGeneration;
    private bool _hasRequestGeneration;
    private int _sectionCount;
    private long _retainedBytes;

    internal int WorldGeneration
    {
        get
        {
            lock (_gate)
                return _worldGeneration;
        }
    }

    internal int ColumnCount
    {
        get
        {
            lock (_gate)
                return _columns.Count;
        }
    }

    internal int SectionCount
    {
        get
        {
            lock (_gate)
                return _sectionCount;
        }
    }

    internal long RetainedBytes
    {
        get
        {
            lock (_gate)
                return _retainedBytes;
        }
    }

    internal int BlockActorCount
    {
        get
        {
            lock (_gate)
                return _blockActors.Count;
        }
    }

    internal void NoteSubChunkRequest(long requestGeneration)
    {
        lock (_gate)
        {
            _requestGeneration = requestGeneration;
            _hasRequestGeneration = true;
        }
    }

    internal bool TryApplyTrailer(int chunkX, int chunkZ, int dimension, int worldGeneration, BedrockLevelChunkTrailer trailer, int payloadBytes)
    {
        ArgumentNullException.ThrowIfNull(trailer);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        if (!BedrockDimensionTable.TryGet(dimension, out int minY, out int height))
            throw new BedrockCodecException($"The chunk dimension {dimension} is not a known Bedrock dimension.");
        if (trailer.BlockSections.Count > MaxSectionsPerColumn)
            throw new InvalidOperationException($"The level chunk keeps more than {MaxSectionsPerColumn} sections.");
        if (payloadBytes > MaxRetainedBytes)
            throw new InvalidOperationException($"The level chunk payload exceeds the retained byte budget.");
        if (trailer.Actors.Count > MaxBlockActors)
            throw new InvalidOperationException($"The level chunk retains more than {MaxBlockActors} block actors.");
        int minSection = BedrockDimensionTable.MinSectionY(minY);
        int expectedSections = BedrockDimensionTable.SectionCount(height);
        var seenSectionY = new HashSet<int>();
        foreach (BedrockChunkSection section in trailer.BlockSections)
        {
            if (section.Layers.Count is < 1 or > 2)
                throw new BedrockCodecException($"The chunk section layer count {section.Layers.Count} is outside the supported range 1 to 2.");
            if (section.SectionY < minSection || section.SectionY >= minSection + expectedSections)
                throw new BedrockCodecException($"The chunk section Y {section.SectionY} is outside dimension {dimension}.");
            if (!seenSectionY.Add(section.SectionY))
                throw new BedrockCodecException($"The level chunk repeats section Y {section.SectionY}.");
        }

        foreach (BedrockBlockActorData actor in trailer.Actors)
        {
            if (actor.Y < minY || actor.Y >= minY + height)
                throw new BedrockCodecException($"The block actor Y {actor.Y} is outside dimension {dimension}.");
            if (actor.Payload.Length > MaxSinglePayloadBytes)
                throw new InvalidOperationException($"The block actor payload exceeds the retained byte budget.");
        }

        lock (_gate)
        {
            if (worldGeneration != _worldGeneration)
                return false;
            var dedupedActors = new Dictionary<BlockActorKey, BedrockBlockActorData>();
            foreach (BedrockBlockActorData actor in trailer.Actors)
                dedupedActors[new BlockActorKey(dimension, actor.X, actor.Y, actor.Z)] = actor;
            var key = new ColumnKey(dimension, chunkX, chunkZ);
            bool isNew = !_columns.TryGetValue(key, out Column? existing);
            if (isNew && trailer.BlockSections.Count > 0 && _columns.Count >= MaxColumns)
                throw new InvalidOperationException($"The Bedrock world store keeps more than {MaxColumns} columns.");
            int replacing = existing?.Sections.Count ?? 0;
            int distinct = trailer.BlockSections.Select(static section => section.SectionY).Distinct().Count();
            if (_sectionCount - replacing + distinct > MaxSectionsTotal)
                throw new InvalidOperationException($"The Bedrock world store keeps more than {MaxSectionsTotal} sections.");
            long actorDelta = 0;
            int freshActors = 0;
            foreach ((BlockActorKey actorKey, BedrockBlockActorData actor) in dedupedActors)
            {
                bool actorIsNew = !_blockActors.TryGetValue(actorKey, out byte[]? current);
                if (actorIsNew)
                    freshActors++;
                actorDelta += actor.Payload.Length - (current?.Length ?? 0);
            }

            if (_blockActors.Count + freshActors > MaxBlockActors)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxBlockActors} block actors.");
            if (_retainedBytes - (existing?.PayloadBytes ?? 0) + payloadBytes + actorDelta > MaxRetainedBytes)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxRetainedBytes} bytes.");
            if (trailer.BlockSections.Count > 0)
            {
                var column = new Column(dimension, chunkX, chunkZ, payloadBytes);
                int baseShare = payloadBytes / trailer.BlockSections.Count;
                int remainder = payloadBytes % trailer.BlockSections.Count;
                for (int index = 0; index < trailer.BlockSections.Count; index++)
                {
                    BedrockChunkSection section = trailer.BlockSections[index];
                    int share = baseShare + (index < remainder ? 1 : 0);
                    column.Sections[section.SectionY] = new Section(section.SectionY, false, section, share);
                }

                _columns[key] = column;
                _sectionCount += distinct - replacing;
                _retainedBytes += payloadBytes - (existing?.PayloadBytes ?? 0);
            }

            foreach ((BlockActorKey actorKey, BedrockBlockActorData actor) in dedupedActors)
            {
                _blockActors.TryGetValue(actorKey, out byte[]? current);
                _retainedBytes += actor.Payload.Length - (current?.Length ?? 0);
                _blockActors[actorKey] = (byte[])actor.Payload.Clone();
            }

            return true;
        }
    }

    internal bool TryApplyLevelChunk(int chunkX, int chunkZ, int dimension, int worldGeneration, BedrockLevelChunkBody body, int payloadBytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        if (!BedrockDimensionTable.TryGet(dimension, out int minY, out int height))
            throw new BedrockCodecException($"The chunk dimension {dimension} is not a known Bedrock dimension.");
        if (body.Sections.Count > MaxSectionsPerColumn)
            throw new InvalidOperationException($"The level chunk keeps more than {MaxSectionsPerColumn} sections.");
        if (payloadBytes > MaxRetainedBytes)
            throw new InvalidOperationException($"The level chunk payload exceeds the retained byte budget.");
        int minSection = BedrockDimensionTable.MinSectionY(minY);
        int expectedSections = BedrockDimensionTable.SectionCount(height);
        var seenSectionY = new HashSet<int>();
        foreach (BedrockChunkSection section in body.Sections)
        {
            if (section.Layers.Count is < 1 or > 2)
                throw new BedrockCodecException($"The chunk section layer count {section.Layers.Count} is outside the supported range 1 to 2.");
            if (section.SectionY < minSection || section.SectionY >= minSection + expectedSections)
                throw new BedrockCodecException($"The chunk section Y {section.SectionY} is outside dimension {dimension}.");
            if (!seenSectionY.Add(section.SectionY))
                throw new BedrockCodecException($"The level chunk repeats section Y {section.SectionY}.");
        }

        lock (_gate)
        {
            if (worldGeneration != _worldGeneration)
                return false;
            if (body.Sections.Count == 0)
                return true;
            var key = new ColumnKey(dimension, chunkX, chunkZ);
            bool isNew = !_columns.TryGetValue(key, out Column? existing);
            if (isNew && _columns.Count >= MaxColumns)
                throw new InvalidOperationException($"The Bedrock world store keeps more than {MaxColumns} columns.");
            int replacing = existing?.Sections.Count ?? 0;
            int distinct = body.Sections.Select(static section => section.SectionY).Distinct().Count();
            if (_sectionCount - replacing + distinct > MaxSectionsTotal)
                throw new InvalidOperationException($"The Bedrock world store keeps more than {MaxSectionsTotal} sections.");
            if (_retainedBytes - (existing?.PayloadBytes ?? 0) + payloadBytes > MaxRetainedBytes)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxRetainedBytes} bytes.");

            var column = new Column(dimension, chunkX, chunkZ, payloadBytes);
            if (body.Sections.Count > 0)
            {
                int baseShare = payloadBytes / body.Sections.Count;
                int remainder = payloadBytes % body.Sections.Count;
                for (int index = 0; index < body.Sections.Count; index++)
                {
                    BedrockChunkSection section = body.Sections[index];
                    int share = baseShare + (index < remainder ? 1 : 0);
                    column.Sections[section.SectionY] = new Section(section.SectionY, false, section, share);
                }
            }

            _columns[key] = column;
            _sectionCount += distinct - replacing;
            _retainedBytes += payloadBytes - (existing?.PayloadBytes ?? 0);
            return true;
        }
    }

    internal bool TryApplySubChunkEntries(BedrockSubChunkPosition center, int dimension, int worldGeneration, long requestGeneration, IReadOnlyList<BedrockSubChunkEntry> matched)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(matched);
        if (!BedrockDimensionTable.TryGet(dimension, out int minY, out int height))
            throw new BedrockCodecException($"The sub-chunk dimension {dimension} is not a known Bedrock dimension.");
        int minSection = BedrockDimensionTable.MinSectionY(minY);
        int expectedSections = BedrockDimensionTable.SectionCount(height);

        var decoded = new List<PendingSection>(matched.Count);
        var pendingActors = new List<BedrockBlockActorData>();
        foreach (BedrockSubChunkEntry entry in matched)
        {
            int sectionX = center.X + entry.Offset.Dx;
            int sectionY = center.Y + entry.Offset.Dy;
            int sectionZ = center.Z + entry.Offset.Dz;
            if (sectionY < minSection || sectionY >= minSection + expectedSections)
                throw new BedrockCodecException($"The sub-chunk section Y {sectionY} is outside dimension {dimension}.");
            switch (entry.Result)
            {
                case BedrockSubChunkResult.SuccessAllAir:
                    decoded.Add(new PendingSection(sectionX, sectionY, sectionZ, null, 0, true));
                    break;
                case BedrockSubChunkResult.Success when entry.Payload is not null:
                    if (entry.Payload.Length > MaxSinglePayloadBytes)
                        throw new InvalidOperationException($"The sub-chunk payload exceeds the retained byte budget.");
                    (BedrockChunkSection section, IReadOnlyList<BedrockBlockActorData> actors) = BedrockChunkPayloadCodecs.DecodeSubChunkPayload(entry.Payload);
                    if (section.SectionY != sectionY)
                        throw new BedrockCodecException($"The sub-chunk payload section Y {section.SectionY} does not match its offset section Y {sectionY}.");
                    decoded.Add(new PendingSection(sectionX, sectionY, sectionZ, section, entry.Payload.Length, false));
                    foreach (BedrockBlockActorData actor in actors)
                    {
                        if (actor.Y < minY || actor.Y >= minY + height)
                            throw new BedrockCodecException($"The block actor Y {actor.Y} is outside dimension {dimension}.");
                        if (actor.Payload.Length > MaxSinglePayloadBytes)
                            throw new InvalidOperationException($"The block actor payload exceeds the retained byte budget.");
                        pendingActors.Add(actor);
                    }

                    break;
                case BedrockSubChunkResult.Success:
                    throw new BedrockCodecException("The successful sub-chunk entry carries no payload.");
                default:
                    break;
            }
        }

        lock (_gate)
        {
            if (worldGeneration != _worldGeneration)
                return false;
            if (!_hasRequestGeneration || requestGeneration != _requestGeneration)
                return false;
            var deduped = new Dictionary<(int SectionX, int SectionY, int SectionZ), PendingSection>();
            foreach (PendingSection pending in decoded)
                deduped[(pending.SectionX, pending.SectionY, pending.SectionZ)] = pending;

            var affected = new Dictionary<ColumnKey, List<PendingSection>>();
            foreach (PendingSection pending in deduped.Values)
            {
                var key = new ColumnKey(dimension, pending.SectionX, pending.SectionZ);
                if (!affected.TryGetValue(key, out List<PendingSection>? list))
                    affected[key] = list = [];
                list.Add(pending);
            }

            int freshColumns = 0;
            foreach (ColumnKey key in affected.Keys)
            {
                if (!_columns.ContainsKey(key))
                    freshColumns++;
            }

            if (_columns.Count + freshColumns > MaxColumns)
                throw new InvalidOperationException($"The Bedrock world store keeps more than {MaxColumns} columns.");
            int addedSections = 0;
            long addedBytes = 0;
            foreach ((ColumnKey key, List<PendingSection> sections) in affected)
            {
                _columns.TryGetValue(key, out Column? existing);
                int fresh = 0;
                foreach (int sectionY in sections.Select(static pending => pending.SectionY).Distinct())
                {
                    if (existing is null || !existing.Sections.ContainsKey(sectionY))
                        fresh++;
                }

                addedSections += fresh;
                foreach (PendingSection pending in sections)
                {
                    int oldLength = existing is not null && existing.Sections.TryGetValue(pending.SectionY, out Section? oldSection) ? oldSection.PayloadBytes : 0;
                    addedBytes += pending.PayloadLength - oldLength;
                }

                if (existing is not null && existing.Sections.Count + fresh > MaxSectionsPerColumn)
                    throw new InvalidOperationException($"The Bedrock world store keeps more than {MaxSectionsPerColumn} sections per column.");
            }

            if (_sectionCount + addedSections > MaxSectionsTotal)
                throw new InvalidOperationException($"The Bedrock world store keeps more than {MaxSectionsTotal} sections.");
            if (_retainedBytes + addedBytes > MaxRetainedBytes)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxRetainedBytes} bytes.");
            var dedupedActors = new Dictionary<BlockActorKey, BedrockBlockActorData>();
            foreach (BedrockBlockActorData actor in pendingActors)
                dedupedActors[new BlockActorKey(dimension, actor.X, actor.Y, actor.Z)] = actor;
            long actorDelta = 0;
            int freshActors = 0;
            foreach ((BlockActorKey actorKey, BedrockBlockActorData actor) in dedupedActors)
            {
                bool actorIsNew = !_blockActors.TryGetValue(actorKey, out byte[]? current);
                if (actorIsNew)
                    freshActors++;
                actorDelta += actor.Payload.Length - (current?.Length ?? 0);
            }

            if (_blockActors.Count + freshActors > MaxBlockActors)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxBlockActors} block actors.");
            if (_retainedBytes + addedBytes + actorDelta > MaxRetainedBytes)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxRetainedBytes} bytes.");

            foreach ((ColumnKey key, List<PendingSection> sections) in affected)
            {
                if (!_columns.TryGetValue(key, out Column? column))
                {
                    column = new Column(dimension, key.ChunkX, key.ChunkZ, 0);
                    _columns[key] = column;
                }

                foreach (PendingSection pending in sections)
                {
                    bool isNew = !column.Sections.TryGetValue(pending.SectionY, out Section? oldSection);
                    int oldLength = oldSection?.PayloadBytes ?? 0;
                    column.Sections[pending.SectionY] = pending.Decoded is null
                        ? new Section(pending.SectionY, true, null, 0)
                        : new Section(pending.SectionY, false, pending.Decoded, pending.PayloadLength);
                    if (isNew)
                        _sectionCount++;
                    column.PayloadBytes += pending.PayloadLength - oldLength;
                }
            }

            _retainedBytes += addedBytes;
            foreach ((BlockActorKey actorKey, BedrockBlockActorData actor) in dedupedActors)
            {
                _blockActors.TryGetValue(actorKey, out byte[]? current);
                _retainedBytes += actor.Payload.Length - (current?.Length ?? 0);
                _blockActors[actorKey] = (byte[])actor.Payload.Clone();
            }

            return true;
        }
    }

    internal bool TryApplyUpdateBlock(int x, int y, int z, int dimension, int worldGeneration, uint blockRuntimeId, uint layer)
    {
        if (layer is not (0 or 1))
            throw new BedrockCodecException($"The update block layer {layer} is outside the supported range 0 to 1.");
        if (!BedrockDimensionTable.TryGet(dimension, out int minY, out int height))
            throw new BedrockCodecException($"The block dimension {dimension} is not a known Bedrock dimension.");
        if (y < minY || y >= minY + height)
            throw new BedrockCodecException($"The block Y {y} is outside dimension {dimension}.");

        lock (_gate)
        {
            if (worldGeneration != _worldGeneration)
                return false;
            if (!_columns.TryGetValue(new ColumnKey(dimension, FloorDiv16(x), FloorDiv16(z)), out Column? column))
                return false;
            int sectionY = SectionY(y);
            if (!column.Sections.TryGetValue(sectionY, out Section? section) || section.IsKnownAir || section.Decoded is null)
                return false;
            BedrockChunkSection decoded = section.Decoded;
            if (layer >= (uint)decoded.Layers.Count)
                return false;
            int localX = x & 15;
            int localZ = z & 15;
            int localY = y - sectionY * 16;
            int index = (localX << 8) | (localZ << 4) | localY;
            BedrockBlockStorage storage = decoded.Layers[(int)layer];
            int value = unchecked((int)blockRuntimeId);
            int paletteIndex = Array.IndexOf(storage.Palette, value);
            int[] palette;
            int[] cells = (int[])storage.Cells.Clone();
            int bits = storage.BitsPerBlock;
            if (paletteIndex < 0)
            {
                if (storage.Palette.Length >= BedrockBlockStorage.CellCount)
                    throw new InvalidOperationException("The block palette keeps more than 4096 entries.");
                palette = new int[storage.Palette.Length + 1];
                Array.Copy(storage.Palette, palette, storage.Palette.Length);
                palette[^1] = value;
                paletteIndex = palette.Length - 1;
                bits = RequiredBits(palette.Length, storage.BitsPerBlock);
            }
            else
            {
                palette = (int[])storage.Palette.Clone();
            }

            cells[index] = paletteIndex;
            var updated = new BedrockBlockStorage(bits, true, palette, cells);
            var layers = new BedrockBlockStorage[decoded.Layers.Count];
            for (int i = 0; i < layers.Length; i++)
                layers[i] = i == (int)layer ? updated : decoded.Layers[i];
            var replaced = new BedrockChunkSection(decoded.SectionY, layers);
            byte[] encoded = BedrockChunkPayloadCodecs.EncodeSection(replaced);
            if (encoded.Length > MaxSinglePayloadBytes)
                throw new InvalidOperationException("The updated section exceeds the retained byte budget.");
            int oldLength = section.PayloadBytes;
            long delta = encoded.Length - oldLength;
            if (_retainedBytes + delta > MaxRetainedBytes)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxRetainedBytes} bytes.");
            column.Sections[sectionY] = new Section(sectionY, false, replaced, encoded.Length);
            column.PayloadBytes += delta;
            _retainedBytes += delta;
            return true;
        }
    }

    internal BedrockSubChunkBlocksApplyResult TryApplySubChunkBlocks(int dimension, int worldGeneration, BedrockUpdateSubChunkBlocksPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        int applied = 0;
        int refused = 0;
        foreach (BedrockSubChunkBlockChange change in packet.Blocks)
        {
            if (TryApplyUpdateBlock(change.X, change.Y, change.Z, dimension, worldGeneration, change.BlockRuntimeId, 0))
                applied++;
            else
                refused++;
        }

        foreach (BedrockSubChunkBlockChange change in packet.Extra)
        {
            if (TryApplyUpdateBlock(change.X, change.Y, change.Z, dimension, worldGeneration, change.BlockRuntimeId, 1))
                applied++;
            else
                refused++;
        }

        return new BedrockSubChunkBlocksApplyResult(applied, refused);
    }

    internal bool TryRetainBlockActor(int x, int y, int z, int dimension, int worldGeneration, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!BedrockDimensionTable.TryGet(dimension, out int minY, out int height))
            throw new BedrockCodecException($"The block actor dimension {dimension} is not a known Bedrock dimension.");
        if (y < minY || y >= minY + height)
            throw new BedrockCodecException($"The block actor Y {y} is outside dimension {dimension}.");
        if (payload.Length > MaxSinglePayloadBytes)
            throw new InvalidOperationException($"The block actor payload exceeds the retained byte budget.");

        lock (_gate)
        {
            if (worldGeneration != _worldGeneration)
                return false;
            var key = new BlockActorKey(dimension, x, y, z);
            bool isNew = !_blockActors.TryGetValue(key, out byte[]? existing);
            if (isNew && _blockActors.Count >= MaxBlockActors)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxBlockActors} block actors.");
            if (_retainedBytes - (existing?.Length ?? 0) + payload.Length > MaxRetainedBytes)
                throw new InvalidOperationException($"The Bedrock world store retains more than {MaxRetainedBytes} bytes.");
            _retainedBytes += payload.Length - (existing?.Length ?? 0);
            _blockActors[key] = (byte[])payload.Clone();
            return true;
        }
    }

    internal void InvalidateWorld()
    {
        lock (_gate)
        {
            _worldGeneration++;
            _columns.Clear();
            _blockActors.Clear();
            _sectionCount = 0;
            _retainedBytes = 0;
            _hasRequestGeneration = false;
        }
    }

    internal BedrockBlockQuery GetBlock(int x, int y, int z, int dimension)
    {
        if (!BedrockDimensionTable.TryGet(dimension, out int minY, out int height))
            return new BedrockBlockQuery(BedrockCellAvailability.Absent, null);
        if (y < minY || y >= minY + height)
            return new BedrockBlockQuery(BedrockCellAvailability.Absent, null);

        lock (_gate)
        {
            if (!_columns.TryGetValue(new ColumnKey(dimension, FloorDiv16(x), FloorDiv16(z)), out Column? column))
                return new BedrockBlockQuery(BedrockCellAvailability.Absent, null);
            int sectionY = SectionY(y);
            if (!column.Sections.TryGetValue(sectionY, out Section? section))
                return new BedrockBlockQuery(BedrockCellAvailability.Absent, null);
            if (section.IsKnownAir)
                return new BedrockBlockQuery(BedrockCellAvailability.KnownAir, null);
            BedrockChunkSection decoded = section.Decoded!;
            int localY = y - sectionY * 16;
            int index = ((x & 15) << 8) | ((z & 15) << 4) | localY;
            BedrockBlockStorage primary = decoded.Layers[0];
            var layers = new BedrockBlockLayers(
                BedrockBlockStateIdentity.FromPalette(primary.IsRuntime, primary.Palette[primary.Cells[index]]),
                decoded.Layers.Count > 1
                    ? BedrockBlockStateIdentity.FromPalette(decoded.Layers[1].IsRuntime, decoded.Layers[1].Palette[decoded.Layers[1].Cells[index]])
                    : null);
            return new BedrockBlockQuery(BedrockCellAvailability.Complete, layers);
        }
    }

    internal BedrockColumnAvailability GetColumnAvailability(int chunkX, int chunkZ, int dimension)
    {
        if (!BedrockDimensionTable.TryGet(dimension, out _, out int height))
            return BedrockColumnAvailability.Absent;

        lock (_gate)
        {
            if (!_columns.TryGetValue(new ColumnKey(dimension, chunkX, chunkZ), out Column? column))
                return BedrockColumnAvailability.Absent;
            return column.Sections.Count >= BedrockDimensionTable.SectionCount(height)
                ? BedrockColumnAvailability.Complete
                : BedrockColumnAvailability.Partial;
        }
    }

    internal BedrockWorldSnapshot CaptureSnapshot()
    {
        lock (_gate)
            return new BedrockWorldSnapshot(_worldGeneration, _columns.Count, _sectionCount, _retainedBytes, _blockActors.Count);
    }

    private static int FloorDiv16(int value) => value >= 0 ? value / 16 : (value - 15) / 16;

    private static int SectionY(int y) => FloorDiv16(y);

    private static int RequiredBits(int paletteSize, int current)
    {
        if (paletteSize <= 1)
            return 0;
        foreach (int width in new[] { 1, 2, 3, 4, 5, 6, 8, 16 })
        {
            if ((1 << width) >= paletteSize)
                return Math.Max(width, current);
        }

        throw new InvalidOperationException($"The block palette size {paletteSize} exceeds the supported width.");
    }

    private sealed record PendingSection(int SectionX, int SectionY, int SectionZ, BedrockChunkSection? Decoded, int PayloadLength, bool IsKnownAir);

    private readonly record struct ColumnKey(int Dimension, int ChunkX, int ChunkZ);

    private readonly record struct BlockActorKey(int Dimension, int X, int Y, int Z);

    private sealed class Column
    {
        internal Column(int dimension, int chunkX, int chunkZ, long payloadBytes)
        {
            Dimension = dimension;
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            PayloadBytes = payloadBytes;
        }

        internal int Dimension { get; }

        internal int ChunkX { get; }

        internal int ChunkZ { get; }

        internal long PayloadBytes { get; set; }

        internal Dictionary<int, Section> Sections { get; } = new();
    }

    private sealed class Section
    {
        internal Section(int sectionY, bool isKnownAir, BedrockChunkSection? decoded, int payloadBytes)
        {
            SectionY = sectionY;
            IsKnownAir = isKnownAir;
            Decoded = decoded;
            PayloadBytes = payloadBytes;
        }

        internal int SectionY { get; }

        internal bool IsKnownAir { get; }

        internal BedrockChunkSection? Decoded { get; }

        internal int PayloadBytes { get; }
    }
}
