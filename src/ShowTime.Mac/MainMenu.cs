using AppKit;
using ObjCRuntime;

namespace ShowTime.Mac;

/// <summary>
/// The application menu. ShowTime lives in the menu bar and shows no menus of its own, but their shortcuts work
/// while one of its windows is in front: ⌘S, ⌘Z, ⌘C, ⌘W, ⌘Q, and the text editing ones. Each item sends its action
/// down the responder chain (the editor, its text field, its window).
/// </summary>
internal static class MainMenu
{
    public static NSMenu Create()
    {
        var main = new NSMenu();
        Add(main, "ShowTime",
            Item("Quitter ShowTime", "terminate:", "q"));
        Add(main, "Fichier",
            Item("Fermer", "performClose:", "w"),
            Item("Enregistrer", "saveDocument:", "s"),
            Item("Enregistrer sous…", "saveDocumentAs:", "S"));
        Add(main, "Édition",
            Item("Annuler", "undo:", "z"),
            Item("Rétablir", "redo:", "Z"),
            NSMenuItem.SeparatorItem,
            Item("Couper", "cut:", "x"),
            Item("Copier", "copy:", "c"),
            Item("Coller", "paste:", "v"),
            Item("Tout sélectionner", "selectAll:", "a"));
        Add(main, "Présentation",
            Item("Taille réelle", "actualSize:", "1"),
            Item("Ajuster à la fenêtre", "fitToWindow:", "0"),
            Item("Zoom avant", "zoomIn:", "="),
            Item("Zoom arrière", "zoomOut:", "-"));
        return main;
    }

    private static NSMenuItem Item(string title, string action, string key) => new(title, new Selector(action), key);

    private static void Add(NSMenu main, string title, params NSMenuItem[] items)
    {
        var menu = new NSMenu(title);
        foreach (var item in items)
            menu.AddItem(item);
        main.AddItem(new NSMenuItem(title) { Submenu = menu });
    }
}
