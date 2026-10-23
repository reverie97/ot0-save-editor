using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Ot0Save;

namespace Ot0Gui;

sealed class MainViewModel : Observable, IDisposable
{
    static readonly string[] JobNames = ["", "Warrior", "Apothecary", "Thief", "Scholar", "Cleric", "Hunter", "Merchant", "Dancer"];

    readonly GameData data = GameData.Load();
    readonly Settings settings = Settings.Load();
    readonly DispatcherTimer gameCheck = new() { Interval = TimeSpan.FromSeconds(3) };
    SaveSession? session;

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => MatchesFilter((ItemRow)o);
        ItemTypes = [AllTypes, .. data.Items.Values.Select(i => i.Type ?? "").Where(t => t.Length > 0).Distinct().Order()];

        bool open() => session is not null && !Busy;
        OpenFileCommand = new Command(OpenFile, () => !Busy);
        ReloadCommand = new Command(() => _ = Open(session!.Path), open);
        SaveCommand = new Command(() => _ = Save(), () => open() && Dirty);
        MaxMoneyCommand = new Command(() => Money = 9_999_999, open);
        PartyJpCommand = new Command(() => ForParty(c => c.Jp = PartyJp), open);
        PartyMaxCommand = new Command(() => ForParty(c => c.SetAll(max: true)), open);
        PartyClearCommand = new Command(() => ForParty(c => c.SetAll(max: false)), open);
        AddItemCommand = new Command(AddItem, open);
        VerifyCommand = new Command(() => _ = Verify(), open);
        ExportCommand = new Command(ExportJson, open);
        BackupsCommand = new Command(OpenBackups, () => session is not null);
        OodleCommand = new Command(PickOodle, () => !Busy);
        AboutCommand = new Command(About);

        gameCheck.Tick += (_, _) => GameRunning = SaveLocator.GameIsRunning();
        gameCheck.Start();
        GameRunning = SaveLocator.GameIsRunning();
    }

    public ObservableCollection<SlotRow> Slots { get; } = [];
    public ObservableCollection<CharacterRow> Characters { get; } = [];
    public ObservableCollection<ItemRow> Items { get; } = [];
    public ICollectionView ItemsView { get; }

    public ICommand OpenFileCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand MaxMoneyCommand { get; }
    public ICommand PartyJpCommand { get; }
    public ICommand PartyMaxCommand { get; }
    public ICommand PartyClearCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand BackupsCommand { get; }
    public ICommand OodleCommand { get; }
    public ICommand AboutCommand { get; }

    bool loaded;
    public bool Loaded { get => loaded; private set => Set(ref loaded, value); }

    // WPF only re-checks menu and button state after user input, so nudge it when these change.
    bool busy;
    public bool Busy
    {
        get => busy;
        private set { if (Set(ref busy, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    bool dirty;
    public bool Dirty
    {
        get => dirty;
        private set
        {
            if (!Set(ref dirty, value)) return;
            Raise(nameof(Title));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string FileName => session is null ? "no file" : Path.GetFileName(session.Path);
    public string Title => session is null ? "OT0 Save Editor" : $"OT0 Save Editor - {FileName}{(Dirty ? " *" : "")}";

    string status = "Ready.";
    public string Status { get => status; private set => Set(ref status, value); }

    bool statusIsError;
    public bool StatusIsError { get => statusIsError; private set => Set(ref statusIsError, value); }

    bool gameRunning;
    public bool GameRunning { get => gameRunning; private set => Set(ref gameRunning, value); }

    public string PlayTime { get; private set; } = "";
    public string Protagonist { get; private set; } = "";

    public int Money
    {
        get => session?.Save.Money ?? 0;
        set
        {
            if (session is null || session.Save.Money == value) return;
            session.Save.Money = Math.Max(0, value);
            Raise();
            Changed();
        }
    }

    // Picking a slot in the dropdown opens it. Fill() sets the field directly so it doesn't loop.
    SlotRow? selectedSlot;
    public SlotRow? SelectedSlot
    {
        get => selectedSlot;
        set
        {
            if (value is null || value == selectedSlot) return;
            if (session is not null && string.Equals(value.Path, session.Path, StringComparison.OrdinalIgnoreCase)) return;
            _ = Open(value.Path);
        }
    }

    int partyJp = 30_000;
    public int PartyJp { get => partyJp; set => Set(ref partyJp, value); }

    string itemFilter = "";
    public string ItemFilter
    {
        get => itemFilter;
        set { if (Set(ref itemFilter, value)) ItemsView.Refresh(); }
    }

    public const string AllTypes = "All types";
    public IReadOnlyList<string> ItemTypes { get; }

    string typeFilter = AllTypes;
    public string TypeFilter
    {
        get => typeFilter;
        set { if (Set(ref typeFilter, value ?? AllTypes)) ItemsView.Refresh(); }
    }

    bool showAllItems;
    public bool ShowAllItems
    {
        get => showAllItems;
        set { if (Set(ref showAllItems, value)) ItemsView.Refresh(); }
    }

    public string ItemSummary => session is null ? "" : $"{session.Save.Items.All.Count():N0} owned, {data.Items.Count:N0} known";

    string newItemId = "";
    public string NewItemId { get => newItemId; set => Set(ref newItemId, value); }

    int newItemCount = 99;
    public int NewItemCount { get => newItemCount; set => Set(ref newItemCount, value); }

    public async Task Start(string? file)
    {
        var path = file ?? SaveLocator.Newest();
        if (path is null)
        {
            Report("No saves found in Documents\\My Games. Use File > Open.", error: true);
            return;
        }
        await Open(path);
    }

    public async Task Open(string path)
    {
        if (Dirty && !Confirm("Discard unsaved changes?"))
        {
            Raise(nameof(SelectedSlot));
            return;
        }

        Busy = true;
        Report($"Opening {Path.GetFileName(path)}...");
        try
        {
            var opened = await Task.Run(() => SaveSession.Open(path, settings.OodlePath));
            session?.Dispose();
            session = opened;
            Fill();
            Report("Ready.");
        }
        catch (FileNotFoundException e) when (e.Message.Contains("Oodle"))
        {
            Report("No Oodle DLL found.", error: true);
            if (Confirm("Couldn't find an Oodle DLL (oo2core*.dll), which is needed to read the save.\n\nLocate one now?"))
                PickOodle();
        }
        catch (Exception e)
        {
            Report($"Couldn't open {Path.GetFileName(path)}: {e.Message}", error: true);
        }
        finally
        {
            Busy = false;
        }
    }

    void Fill()
    {
        var save = session!.Save;
        var party = save.Party;

        Characters.Clear();
        var inParty = party.Select(save.Character).OfType<Character>();
        var bench = save.Characters.Where(c => !party.Contains(c.Id) && !c.IsGuest && !c.IsHidden);
        foreach (var c in inParty.Concat(bench))
        {
            var name = data.CharacterName(save, c.Id);
            Characters.Add(new CharacterRow(c, name == c.Id.ToString() ? "" : name, party.Contains(c.Id), Changed));
        }

        // Every known item plus anything in the save the list doesn't know about. The filter
        // hides what you don't own unless "Show items I don't have" is ticked.
        Items.Clear();
        var inventory = save.Items;
        foreach (var id in data.Items.Keys.Union(inventory.All.Select(i => i.Id)).Order())
            Items.Add(new ItemRow(inventory, id, data, ItemsChanged, ConfirmAdd));

        Slots.Clear();
        foreach (var slot in Directory.GetFiles(Path.GetDirectoryName(session.Path)!, "SaveData*.sav").Order())
            Slots.Add(new SlotRow(slot));
        selectedSlot = Slots.FirstOrDefault(s => string.Equals(s.Path, session.Path, StringComparison.OrdinalIgnoreCase));

        var t = save.PlayTime;
        PlayTime = $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        int job = save.ProtagonistJob;
        Protagonist = job > 0 && job < JobNames.Length ? $"{save.ProtagonistName} ({JobNames[job]})" : save.ProtagonistName;

        Dirty = false;
        Loaded = true;
        foreach (var name in new[] { nameof(SelectedSlot), nameof(Money), nameof(PlayTime), nameof(Protagonist), nameof(FileName), nameof(Title), nameof(ItemSummary) })
            Raise(name);
        CommandManager.InvalidateRequerySuggested();
    }

    void Changed() => Dirty = true;

    void ItemsChanged()
    {
        Dirty = true;
        Raise(nameof(ItemSummary));
    }

    bool ConfirmAdd(ItemRow row)
    {
        if (!data.IsRiskyToAdd(row.Id))
            return true;
        var what = row.Name.Length > 0 ? $"{row.Name} is a {row.Type.ToLowerInvariant()}" : $"Item {row.Id} isn't in the item list";
        return Confirm($"{what}. Adding one you haven't earned can mess with story or Wishvale progress.\n\nAdd it anyway?");
    }

    async Task Save()
    {
        if (session is null) return;
        Busy = true;
        Report("Saving...");
        try
        {
            var backup = await Task.Run(() => session.Commit(backup: true));
            Dirty = false;
            Report($"Saved. Backup in {Path.GetFileName(backup)}");
        }
        catch (Exception e)
        {
            Report("Not saved.", error: true);
            MessageBox.Show(e.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Busy = false;
        }
    }

    void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "OT0 saves (SaveData*.sav)|SaveData*.sav|All .sav files|*.sav",
            InitialDirectory = session is null ? SaveLocator.SaveFolders().FirstOrDefault() ?? "" : Path.GetDirectoryName(session.Path),
        };
        if (dialog.ShowDialog() == true)
            _ = Open(dialog.FileName);
    }

    void ForParty(Action<CharacterRow> action)
    {
        foreach (var c in Characters.Where(c => c.InParty))
            action(c);
    }

    // Takes an ID or a name. Names that match more than one item (there are 8 Crested Keys)
    // need the ID.
    void AddItem()
    {
        if (session is null) return;
        var text = NewItemId.Trim();
        if (text.Length == 0) return;

        var hits = data.FindItems(text);
        if (hits.Count != 1)
        {
            Report(hits.Count == 0
                ? $"No item called '{text}'."
                : $"'{text}' matches {hits.Count} items ({string.Join(", ", hits.Take(5).Select(h => $"{h} {data.ItemName(h)}"))}{(hits.Count > 5 ? ", ..." : "")}). Use the ID.",
                error: true);
            return;
        }

        int id = hits[0];
        var row = Items.FirstOrDefault(i => i.Id == id);
        if (row is null)
        {
            // An ID that's in neither the save nor the list. The row's own check asks about it.
            row = new ItemRow(session.Save.Items, id, data, ItemsChanged, ConfirmAdd);
            Items.Insert(Items.TakeWhile(i => i.Id < id).Count(), row);
        }

        row.Count = NewItemCount;
        if (row.Count != NewItemCount)
            return; // declined

        var name = row.Name.Length > 0 ? row.Name : id.ToString();
        Report($"{name} set to {NewItemCount}.");
        NewItemId = "";
    }

    async Task Verify()
    {
        if (session is null) return;
        Busy = true;
        Report("Checking...");
        try
        {
            var problem = await Task.Run(session.CheckRoundTrip);
            Report("Ready.");
            if (problem is null)
                MessageBox.Show($"The save as it was opened rebuilds byte for byte ({session.OriginalGvas.Length:N0} bytes).",
                    "Check save", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(problem, "Check save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception e)
        {
            Report("Check failed.", error: true);
            MessageBox.Show(e.Message, "Check save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Busy = false;
        }
    }

    void ExportJson()
    {
        if (session is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = Path.GetFileNameWithoutExtension(session.Path) + ".json",
        };
        if (dialog.ShowDialog() != true) return;

        var json = JsonDump.ToJson(session.Save.Gvas.Root)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        Report($"Exported {Path.GetFileName(dialog.FileName)}.");
    }

    void OpenBackups()
    {
        if (session is null) return;
        var parent = Path.GetDirectoryName(Path.GetDirectoryName(session.Path))!;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parent}\"") { UseShellExecute = true });
    }

    void PickOodle()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Oodle DLL (currently: {session?.Oodle.DllPath ?? settings.OodlePath ?? "none"})",
            Filter = "Oodle DLL (oo2core*.dll)|oo2core*.dll|DLL files|*.dll",
        };
        if (dialog.ShowDialog() != true) return;

        settings.OodlePath = dialog.FileName;
        settings.Save();
        _ = session is not null ? Open(session.Path) : Start(null);
    }

    void About()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        MessageBox.Show(
            $"OT0 Save Editor {version}\n\n" +
            "Save editor for Octopath Traveler 0 (Steam). Money, JP, stat bonuses, items.\n" +
            "Every save makes a backup of the whole SaveGames folder first.\n\n" +
            $"Oodle: {session?.Oodle.DllPath ?? "not loaded"}\n\n" +
            "MIT license. Not affiliated with Square Enix.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    bool MatchesFilter(ItemRow row)
    {
        if (!ShowAllItems && !row.Owned)
            return false;
        if (TypeFilter != AllTypes && row.Type != TypeFilter)
            return false;

        var f = ItemFilter.Trim();
        return f.Length == 0
            || row.Id.ToString().Contains(f)
            || row.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || row.Notes.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    void Report(string message, bool error = false)
    {
        Status = message;
        StatusIsError = error;
    }

    static bool Confirm(string message) =>
        MessageBox.Show(message, "OT0 Save Editor", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Dispose()
    {
        gameCheck.Stop();
        session?.Dispose();
    }
}
