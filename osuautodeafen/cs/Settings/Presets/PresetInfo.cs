using System;
using System.ComponentModel;

namespace osuautodeafen.cs.Settings.Presets;

public class PresetInfo
{
    private bool _isCurrentPreset;
    public bool IsCurrentPreset
    {
        get => _isCurrentPreset;
        set
        {
            if (_isCurrentPreset != value)
            {
                _isCurrentPreset = value;
                OnPropertyChanged(nameof(IsCurrentPreset));
            }
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    public int Index { get; set; }
    public string FullBeatmapName { get; set; } = "";
    public string Artist { get; set; } = "";
    public string BeatmapName { get; set; } = "";
    public string BeatmapDifficulty { get; set; } = "";
    public string BeatmapID { get; set; } = "";
    public string Mapper { get; set; } = "";
    public string StarRating { get; set; } = "";
    public string AverageColor1 { get; set; } = "#000000";
    public string AverageColor2 { get; set; } = "#000000";
    public string AverageColor3 { get; set; } = "#000000";
    public string FilePath { get; set; } = "";
    public string Checksum { get; set; } = "";
    public string BackgroundPath { get; set; } = "";
    public string RankedStatus { get; set; } = "";
}