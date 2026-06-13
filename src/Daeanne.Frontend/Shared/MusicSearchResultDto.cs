namespace DaeanneFrontend.Shared;

public record ChordDto(string Name, string Frets, string Fingers);

public record SongLineDto(List<string> Chords, string? Lyrics);

public record SongSectionDto(string Label, List<SongLineDto> Lines);

public record MusicSearchResultDto(
    string Query,
    string? Title,
    string? Artist,
    string? Key,
    string? Tempo,
    string Source,
    List<ChordDto> Chords,
    List<SongSectionDto> Sections,
    string? Error = null);
