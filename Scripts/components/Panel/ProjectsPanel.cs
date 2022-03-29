using Godot;
using Godot.Collections;
using Godot.Sharp.Extras;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Dir = System.IO.Directory;
using SearchOption = System.IO.SearchOption;
using DateTime = System.DateTime;

public class ProjectsPanel : Panel
{
#region Node Accessors
    [NodePath("VC/MC/HC/ActionButtons")]
    ActionButtons _actionButtons = null;
    [NodePath("VC/SC/MarginContainer/ProjectList/ListView")]
    VBoxContainer _listView = null;
    [NodePath("VC/SC/MarginContainer/ProjectList/GridView")]
    GridContainer _gridView = null;
    [NodePath("VC/SC/MarginContainer/ProjectList/CategoryView")]
    VBoxContainer _categoryView = null;
    [NodePath("VC/MC/HC/ViewToggleButtons")]
    ViewToggleButtons _viewSelector = null;
#endregion

#region Template Scenes
    PackedScene _ProjectLineEntry = GD.Load<PackedScene>("res://components/ProjectLineEntry.tscn");
    PackedScene _ProjectIconEntry = GD.Load<PackedScene>("res://components/ProjectIconEntry.tscn");
    PackedScene _CategoryList = GD.Load<PackedScene>("res://components/CategoryList.tscn");
#endregion

#region Enumerations
    enum View {
        ListView,
        GridView,
        CategoryView
    }
#endregion

#region Private Variables
    CategoryList clFavorites = null;
    CategoryList clUncategorized = null;

    ProjectLineEntry _currentPLE = null;
    ProjectIconEntry _currentPIE = null;

    View _currentView = View.ListView;
    Dictionary<int, CategoryList> _categoryList;
    ProjectPopup _popupMenu = null;
    Array<ProjectFile> _missingProjects = null;

    Array<string> Views = new Array<string> {
        "List View",
        "Icon View",
        "Category View"
    };
#endregion

    Array<Container> _views;

    public override void _Ready()
    {
        this.OnReady();

        _views = new Array<Container>();
        _views.Add(_listView);
        _views.Add(_gridView);
        _views.Add(_categoryView);

        _popupMenu = GD.Load<PackedScene>("res://components/ProjectPopup.tscn").Instance<ProjectPopup>();
        AddChild(_popupMenu);
        _popupMenu.SetAsToplevel(true);

        AppDialogs.ImportProject.Connect("update_projects", this, "PopulateListing");
        AppDialogs.CreateCategory.Connect("update_categories", this, "PopulateListing");
        AppDialogs.RemoveCategory.Connect("update_categories", this, "PopulateListing");
        AppDialogs.EditProject.Connect("project_updated", this, "PopulateListing");
        AppDialogs.CreateProject.Connect("project_created", this, "OnProjectCreated");

        _actionButtons.SetHidden(3);
        _actionButtons.SetHidden(4);
        _categoryList = new Dictionary<int, CategoryList>();
        _missingProjects = new Array<ProjectFile>();

        if (_viewSelector.SelectedView != -1) {
            if (CentralStore.Settings.DefaultView == "Last View Used") {
                int indx = Views.IndexOf(CentralStore.Settings.LastView);
                _viewSelector.SetView(indx);
                OnViewSelector_Clicked(indx);
            } else {
                int indx = Views.IndexOf(CentralStore.Settings.DefaultView);
                _viewSelector.SetView(indx);
                OnViewSelector_Clicked(indx);
            }
        }

        if (CentralStore.Settings.EnableAutoScan) {
            ScanForProjects();
        }

        PopulateListing();
    }


    public ProjectLineEntry NewPLE(ProjectFile pf) {
        ProjectLineEntry ple = _ProjectLineEntry.Instance<ProjectLineEntry>();
        if (_missingProjects.Contains(pf))
            ple.MissingProject = true;
        else if (!ProjectFile.ProjectExists(pf.Location)) {
            _missingProjects.Add(pf);
            ple.MissingProject = true;
        }
        ple.ProjectFile = pf;
        return ple;
    }

    public ProjectIconEntry NewPIE(ProjectFile pf) {
        ProjectIconEntry pie = _ProjectIconEntry.Instance<ProjectIconEntry>();
        if (_missingProjects.Contains(pf))
            pie.MissingProject = true;
        else if (!ProjectFile.ProjectExists(pf.Location)) {
            _missingProjects.Add(pf);
            pie.MissingProject = true;
        }
        pie.ProjectFile = pf;
        return pie;
    }
    
    public CategoryList NewCL(string name) {
        CategoryList clt = _CategoryList.Instance<CategoryList>();
        clt.Toggable = true;
        clt.CategoryName = name;
        return clt;
    }

    void ConnectHandlers(Node inode) {
        if (inode is ProjectLineEntry ple) {
            ple.Connect("Clicked", this, "OnListEntry_Clicked");
            ple.Connect("DoubleClicked", this, "OnListEntry_DoubleClicked");
            ple.Connect("RightClicked", this, "OnListEntry_RightClicked");
            ple.Connect("RightDoubleClicked", this, "OnListEntry_RightDoubleClicked");
            ple.Connect("FavoriteUpdated", this, "OnListEntry_FavoriteUpdated");
        } else if (inode is ProjectIconEntry pie) {
            pie.Connect("Clicked", this, "OnIconEntry_Clicked");
            pie.Connect("DoubleClicked", this, "OnIconEntry_DoubleClicked");
            pie.Connect("RightClicked", this, "OnIconEntry_RightClicked");
            pie.Connect("RightDoubleClicked", this, "OnIconEntry_RightDoubleClicked");
        }
    }

    async void ScanForProjects() {
        Array<string> projects = new Array<string>();
        Array<string> scanDirs = CentralStore.Settings.ScanDirs.Duplicate();
        int i = 0;

        while (i < scanDirs.Count) {
            if (!Dir.Exists(scanDirs[i]))
                scanDirs.RemoveAt(i);
            else
                i++;
        }

        if (scanDirs.Count == 0) {
            var res = AppDialogs.YesNoDialog.ShowDialog("Scan Project Folders","There are currently no valid Directories to scan, would you like to add one?");
            while (!res.IsCompleted)
                await this.IdleFrame();
            
            if (res.Result) {
                AppDialogs.BrowseFolderDialog.CurrentFile = "";
                AppDialogs.BrowseFolderDialog.CurrentPath = CentralStore.Settings.ProjectPath;
                AppDialogs.BrowseFolderDialog.PopupCentered();
                AppDialogs.BrowseFolderDialog.Connect("dir_selected", this, "OnScanProjects_DirSelected");
                return;
            } else
                return;
        }

        foreach(string dir in CentralStore.Settings.ScanDirs) {
            var projs = Dir.EnumerateFiles(dir,"project.godot",SearchOption.AllDirectories);
            foreach(string proj in projs) {
                projects.Add(proj);
            }
        }

        foreach(string projdir in projects) {
            if (!CentralStore.Instance.HasProject(projdir)) {
                ProjectFile pf = ProjectFile.ReadFromFile(projdir);
                if (pf != null) {
                    pf.GodotVersion = CentralStore.Settings.DefaultEngine;
                    CentralStore.Projects.Add(pf);
                }
            }
        }
    }

    void OnScanProjects_DirSelected(string path) {
        CentralStore.Settings.ScanDirs.Clear();
        CentralStore.Settings.ScanDirs.Add(path);
        CentralStore.Instance.SaveDatabase();
        AppDialogs.BrowseFolderDialog.Disconnect("dir_selected", this, "OnScanProjects_DirSelected");
        ScanForProjects();
        PopulateListing();
    }

    public void PopulateListing() {
        ProjectLineEntry ple;
        ProjectIconEntry pie;
        CategoryList clt;

        foreach(Node child in _listView.GetChildren()) {
            child.QueueFree();
        }
        foreach(Node child in _gridView.GetChildren()) {
            child.QueueFree();
        }
        foreach(CategoryList child in _categoryView.GetChildren()) {
            foreach(Node cchild in child.List.GetChildren()) {
                cchild.QueueFree();
            }
            child.QueueFree();
        }

        _categoryList.Clear();

        foreach(Category cat in CentralStore.Categories) {
            clt = NewCL(cat.Name);
            clt.SetMeta("ID",cat.Id);
            clt.Toggled = cat.IsExpanded;
            _categoryList[cat.Id] = clt;
            _categoryView.AddChild(clt);
            clt.Connect("list_toggled", this, "OnCategoryListToggled", new Array { clt });
        }

        clFavorites = NewCL("Favorites");
        clFavorites.SetMeta("ID", -1);
        clFavorites.Toggled = CentralStore.Settings.FavoritesToggled;
        _categoryView.AddChild(clFavorites);
        clFavorites.Connect("list_toggled", this, "OnCategoryListToggled", new Array { clFavorites });

        clUncategorized = NewCL("Un-Categorized");
        clUncategorized.SetMeta("ID",-2);
        clUncategorized.Toggled = CentralStore.Settings.UncategorizedToggled;
        _categoryView.AddChild(clUncategorized);
        clUncategorized.Connect("list_toggled", this, "OnCategoryListToggled", new Array { clUncategorized });

        foreach(ProjectFile pf in SortListing()) {
            ple = NewPLE(pf);
            pie = NewPIE(pf);
            _listView.AddChild(ple);
            _gridView.AddChild(pie);

            ConnectHandlers(ple);
            ConnectHandlers(pie);
            if (pf.CategoryId == -1) {
                if (pf.Favorite) {
                    clt = clFavorites;
                } else {
                    clt = clUncategorized;
                }
            } else {
                if (_categoryList.ContainsKey(pf.CategoryId))
                    clt = _categoryList[pf.CategoryId];
                else
                    clt = clUncategorized;
            }
            ple = clt.AddProject(pf);
            ConnectHandlers(ple);

            if (pf.CategoryId != -1 && pf.Favorite) {
                ple = clFavorites.AddProject(pf);
                ConnectHandlers(ple);
            }
        }
        if (_missingProjects.Count == 0)
            _actionButtons.SetHidden(6);
        else
            _actionButtons.SetVisible(6);
    }

    public void OnProjectCreated(ProjectFile pf) {
        PopulateListing();
        ExecuteEditorProject(pf.GodotVersion, pf.Location.GetBaseDir());
    }

    private void UpdateListExcept(ProjectLineEntry ple) {
        if (_listView.GetChildren().Contains(ple)) {
            foreach (ProjectLineEntry cple in _listView.GetChildren()) {
                if (cple != ple)
                    cple.SelfModulate = new Color("00ffffff");
            }
        } else {
            foreach (CategoryList cl in _categoryView.GetChildren()) {
                foreach(ProjectLineEntry cple in cl.List.GetChildren()) {
                    if (cple != ple)
                        cple.SelfModulate = new Color("00ffffff");
                }
            }
        }
    }

    void OnCategoryListToggled(CategoryList clt) {
        int id = (int)clt.GetMeta("ID");
        if (id == -1 || id == -2) {
            if (id == -1)
                CentralStore.Settings.FavoritesToggled = clt.Toggled;
            else
                CentralStore.Settings.UncategorizedToggled = clt.Toggled;
            CentralStore.Instance.SaveDatabase();
            return;
        }
        Category cat = CentralStore.Categories.Where(x => x.Id == id).FirstOrDefault<Category>();
        if (cat == null)
            return;
        
        cat.IsExpanded = clt.Toggled;
        CentralStore.Instance.SaveDatabase();
    }

    void OnListEntry_Clicked(ProjectLineEntry ple) {
        UpdateListExcept(ple);
        _currentPLE = ple;
    }

    void OnListEntry_DoubleClicked(ProjectLineEntry ple) {
        if (ple.MissingProject)
            return;
        ple.ProjectFile.LastAccessed = DateTime.UtcNow;
        ExecuteEditorProject(ple.GodotVersion, ple.Location.GetBaseDir());
    }

    void OnListEntry_RightClicked(ProjectLineEntry ple) {
        _popupMenu.ProjectLineEntry = ple;
        _popupMenu.ProjectIconEntry = null;
        _popupMenu.Popup_(new Rect2(GetGlobalMousePosition(), _popupMenu.RectSize));
    }

    void OnListEntry_RightDoubleClicked(ProjectLineEntry ple) {

    }

    void OnListEntry_FavoriteUpdated(ProjectLineEntry ple) { 
        PopulateListing();
    }

    private void OnIconEntry_Clicked(ProjectIconEntry pie) {
        UpdateIconsExcept(pie);
        _currentPIE = pie;
    }

    private void OnIconEntry_DoubleClicked(ProjectIconEntry pie)
	{
        if (pie.MissingProject)
            return;
        pie.ProjectFile.LastAccessed = DateTime.UtcNow;
		ExecuteEditorProject(pie.GodotVersion, pie.Location.GetBaseDir());
	}

    void OnIconEntry_RightClicked(ProjectIconEntry pie) {
        _popupMenu.ProjectLineEntry = null;
        _popupMenu.ProjectIconEntry = pie;
        _popupMenu.Popup_(new Rect2(GetGlobalMousePosition(), _popupMenu.RectSize));
    }

    void OnIconEntry_RightDoubleClicked(ProjectIconEntry pie) {

    }


    public async void _IdPressed(int id) {
        ProjectFile pf;
        if (_popupMenu.ProjectLineEntry != null) {
            pf = _popupMenu.ProjectLineEntry.ProjectFile;
        } else {
            pf = _popupMenu.ProjectIconEntry.ProjectFile;
        }
        switch(id) {
            case 0:     // Open Project
                ExecuteEditorProject(pf.GodotVersion, pf.Location.GetBaseDir());
                break;
            case 1:     // Run Project
                ExecuteProject(pf.GodotVersion, pf.Location.GetBaseDir());
                break;
            case 2:     // Show Project Files
                OS.ShellOpen(pf.Location.GetBaseDir());
                break;
            case 3:     // Show Project Data Folder
                string folder = GetProjectDataFolder(pf);
                OS.ShellOpen(folder);
                break;
            case 4:     // Edit Project File
                AppDialogs.EditProject.ShowDialog(pf);
                break;
            case 5:     // Remove Project
                await RemoveProject(pf);
                break;
        }
    }

    private void RemoveMissingProjects() {
        foreach (ProjectFile missing in _missingProjects) {
            CentralStore.Projects.Remove(missing);
        }
        CentralStore.Instance.SaveDatabase();
        _missingProjects.Clear();
        PopulateListing();
    }

	private string GetProjectDataFolder(ProjectFile pf)
	{
		ProjectConfig pc = new ProjectConfig();
		pc.Load(pf.Location);
		string folder = "";
		if (pc.HasSectionKey("application", "config/use_custom_user_dir"))
		{
			if (pc.GetValue("application", "config/use_custom_user_dir") == "true")
			{
#if GODOT_WINDOWS || GODOT_UWP
				folder = OS.GetEnvironment("APPDATA");
#elif GODOT_LINUXBSD || GODOT_X11
                folder = "~/.local/share";
#elif GODOT_MACOS || GODOT_OSX
                folder = "~/Library/Application Support";
#endif
				folder = folder.PlusFile(pc.GetValue("application", "config/custom_user_dir_name"));
			} else {
#if GODOT_WINDOWS || GODOT_UWP
                folder = OS.GetEnvironment("APPDATA").PlusFile("Godot").PlusFile("app_userdata");
#elif GODOT_LINUXBSD || GODOT_X11
                folder = "~/local/share/godot/app_userdata";
#elif GODOT_MACOS || GODOT_OSX
                folder = "~/Library/Application Support/Godot/app_userdata";
#endif
			    folder = folder.PlusFile(pf.Name);                
            }
		}
		else
		{
#if GODOT_WINDOWS || GODOT_UWP
			folder = OS.GetEnvironment("APPDATA").PlusFile("Godot").PlusFile("app_userdata");
#elif GODOT_LINUXBSD || GODOT_X11
            folder = "~/local/share/godot/app_userdata";
#elif GODOT_MACOS || GODOT_OSX
            folder = "~/Library/Application Support/Godot/app_userdata";
#endif
			folder = folder.PlusFile(pf.Name);
		}
        return folder;
	}

	private void ExecuteProject(string godotVersion, string location)
	{
		GodotVersion gv = CentralStore.Instance.FindVersion(godotVersion);
        if (gv == null)
            return;
        
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = gv.GetExecutablePath().GetOSDir();
        psi.Arguments = $"--path \"{location}\"";
        psi.WorkingDirectory = location.GetBaseDir().GetOSDir().NormalizePath();
        psi.UseShellExecute = !CentralStore.Settings.NoConsole;
        psi.CreateNoWindow = CentralStore.Settings.NoConsole;

        Process proc = Process.Start(psi);
	}

	private void UpdateIconsExcept(ProjectIconEntry pie) {
        foreach(ProjectIconEntry cpie in _gridView.GetChildren()) {
            if (cpie != pie)
                cpie.SelfModulate = new Color("00FFFFFF");
        }
    }

	private void ExecuteEditorProject(string godotVersion, string location)
	{
		GodotVersion gv = CentralStore.Instance.FindVersion(godotVersion);
		if (gv == null)
			return;
        
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = gv.GetExecutablePath().GetOSDir();
        psi.Arguments = $"--path \"{location}\" -e";
        psi.WorkingDirectory = location.GetOSDir().NormalizePath();
        psi.UseShellExecute = !CentralStore.Settings.NoConsole;
        psi.CreateNoWindow = CentralStore.Settings.NoConsole;

        Process proc = Process.Start(psi);
        if (CentralStore.Settings.CloseManagerOnEdit) {
            GetTree().Quit(0);
        }
	}

    [SignalHandler("clicked", nameof(_actionButtons))]
	async void OnActionButtons_Clicked(int index) {
        switch (index) {
            case 0: // New Project File
                AppDialogs.CreateProject.ShowDialog();
                break;
            case 1: // Import Project File
                AppDialogs.ImportProject.ShowDialog();
                break;
            case 2: // Scan Project Folder
                ScanForProjects();
                break;
            case 3: // Add Category
                AppDialogs.CreateCategory.ShowDialog();
                break;
            case 4: // Remove Category
                AppDialogs.RemoveCategory.ShowDialog();
                break;
			case 5: // Remove Project (May be removed completely)
				ProjectFile pf = null;
				if (_currentView == View.GridView)
				{
					if (_currentPIE != null)
						pf = _currentPIE.ProjectFile;
				}
				else
				{
					if (_currentPLE != null)
						pf = _currentPLE.ProjectFile;
				}

				if (pf == null)
					return;

				await RemoveProject(pf);
				break;
            case 6:
                var res = AppDialogs.YesNoDialog.ShowDialog("Remove Missing Projects...", "Are you sure you want to remove any missing projects?");
                await res;
                if (res.Result)
                    RemoveMissingProjects();
                break;
		}
    }

	private async Task RemoveProject(ProjectFile pf)
	{
		var task = AppDialogs.YesNoCancelDialog.ShowDialog("Remove Project", $"You are about to remove Project {pf.Name}.\nDo you wish to remove the files as well?",
			"Project and Files", "Just Project");
		while (!task.IsCompleted)
			await this.IdleFrame();
		switch (task.Result)
		{
			case YesNoCancelDialog.ActionResult.FirstAction:
				string path = pf.Location.GetBaseDir();
				RemoveFolders(path);
				CentralStore.Projects.Remove(pf);
				CentralStore.Instance.SaveDatabase();
				PopulateListing();
				break;
			case YesNoCancelDialog.ActionResult.SecondAction:
				CentralStore.Projects.Remove(pf);
				CentralStore.Instance.SaveDatabase();
				PopulateListing();
				break;
			case YesNoCancelDialog.ActionResult.CancelAction:
				AppDialogs.MessageDialog.ShowMessage("Remove Project", "Remove Project has been cancelled.");
				break;
		}
	}

	void RemoveFolders(string path) {
        Directory dir = new Directory();
        if (dir.Open(path) == Error.Ok) {
            dir.ListDirBegin(true, false);
            var filename = dir.GetNext();
            while (filename != "") {
                if (dir.CurrentIsDir()) {
                    RemoveFolders(path.PlusFile(filename).NormalizePath());
                }
                dir.Remove(filename);
                filename = dir.GetNext();
            }
            dir.ListDirEnd();
        }
        dir.Open(path.GetBaseDir());
        dir.Remove(path.GetFile());
    }

    [SignalHandler("Clicked", nameof(_viewSelector))]
    void OnViewSelector_Clicked(int page) {
        for (int i = 0; i < _views.Count; i++) {
            if (i == page)
                _views[i].Show();
            else
                _views[i].Hide();
        }
        if (page == 2) {
            _actionButtons.SetVisible(3);
            _actionButtons.SetVisible(4);
        } else {
            _actionButtons.SetHidden(3);
            _actionButtons.SetHidden(4);
        }
        _currentView = (View)page;
        CentralStore.Settings.LastView = Views[page];
    }

    public Array<ProjectFile> SortListing() {
        Array<ProjectFile> projectFiles = new Array<ProjectFile>();
        var fav = CentralStore.Projects.Where(pf => pf.Favorite == true).OrderByDescending(pf => pf.LastAccessed);
        var non_fav = CentralStore.Projects.Where(pf => pf.Favorite != true).OrderByDescending(pf => pf.LastAccessed);

        foreach(ProjectFile pf in fav)
            projectFiles.Add(pf);
        
        foreach(ProjectFile pf in non_fav)
            projectFiles.Add(pf);
        
        return projectFiles;        
    }
}   
