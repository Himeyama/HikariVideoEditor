namespace VideoEditor;

// PCM data stays on disk; only 10 ms peak buckets are retained in memory.
public sealed class PcmAudio
{
    public required string Path { get; init; }
    public long Offset { get; init; }
    public long Frames { get; init; }
    public int Rate { get; init; }
    public int Channels { get; init; }
    public required float[] Peaks { get; init; }
    public double Duration => (double)Frames / Rate;

    public static PcmAudio Read(string path)
    {
        using BinaryReader reader = new(File.OpenRead(path));
        string Four() => new(reader.ReadChars(4));
        if (Four() != "RIFF") throw new InvalidDataException("WAV形式ではありません。");
        reader.ReadUInt32();
        if (Four() != "WAVE") throw new InvalidDataException("WAV形式ではありません。");
        int channels = 0, rate = 0, bits = 0, format = 0;
        long offset = 0, length = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string tag = Four();
            long size = reader.ReadUInt32(), start = reader.BaseStream.Position;
            if (start + size > reader.BaseStream.Length) throw new InvalidDataException("WAVデータが欠けています。");
            if (tag == "fmt ")
            {
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                if (format == 65534 && size >= 40) { reader.BaseStream.Position = start + 24; format = reader.ReadUInt16(); }
            }
            if (tag == "data") { offset = start; length = size; }
            reader.BaseStream.Position = start + size + (size & 1);
        }
        if (format != 1 || bits != 16 || channels is < 1 or > 2 || rate <= 0 || offset == 0)
            throw new InvalidDataException("16bit PCM音声を読み込めません。");
        long frames = length / (channels * 2);
        float[] peaks = new float[(int)Math.Ceiling((double)frames / rate * 100)];
        reader.BaseStream.Position = offset;
        for (long frame = 0; frame < frames; frame++)
        {
            int bucket = (int)(frame * 100 / rate);
            for (int channel = 0; channel < channels; channel++)
                peaks[bucket] = Math.Max(peaks[bucket], Math.Abs(reader.ReadInt16() / 32768f));
        }
        return new() { Path = path, Offset = offset, Frames = frames, Rate = rate, Channels = channels, Peaks = peaks };
    }

    public double Peak(double start, double end)
    {
        int first = Math.Clamp((int)(start * 100), 0, Peaks.Length);
        int last = Math.Clamp((int)Math.Ceiling(end * 100), first, Peaks.Length);
        float peak = 0;
        for (int i = first; i < last; i++) peak = Math.Max(peak, Peaks[i]);
        return peak;
    }

    public void RenderFade(string destination, EditClip clip)
    {
        long frames = (long)Math.Round(clip.Duration * Rate);
        long first = (long)Math.Round(clip.Trim * Rate);
        if (first + frames > Frames + Rate / 50) throw new ArgumentException("音声の長さを超えています。");
        long bytes = frames * Channels * 2;
        if (bytes > uint.MaxValue - 36) throw new ArgumentException("音声が長すぎます。");
        using BinaryReader input = new(File.OpenRead(Path));
        input.BaseStream.Position = Offset + first * Channels * 2;
        using BinaryWriter output = new(File.Create(destination));
        output.Write("RIFF"u8); output.Write((uint)(bytes + 36)); output.Write("WAVEfmt "u8);
        output.Write(16); output.Write((short)1); output.Write((short)Channels); output.Write(Rate);
        output.Write(Rate * Channels * 2); output.Write((short)(Channels * 2)); output.Write((short)16);
        output.Write("data"u8); output.Write((uint)bytes);
        for (long frame = 0; frame < frames; frame++)
        {
            double gain = clip.AudioGain((double)frame / Rate);
            for (int channel = 0; channel < Channels; channel++)
            {
                short sample = first + frame < Frames ? input.ReadInt16() : (short)0;
                output.Write((short)Math.Round(sample * gain));
            }
        }
    }
}
