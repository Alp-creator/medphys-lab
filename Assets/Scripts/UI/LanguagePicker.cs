using UnityEngine.UIElements;
using MedPhys.Core;
using static MedPhys.Core.Loc;

namespace MedPhys.UI
{
    /// <summary>Full-screen language chooser (native names) opened from the top bar and the main menu.</summary>
    public static class LanguagePicker
    {
        public static void Open()
        {
            var overlay = new VisualElement(); overlay.AddToClassList("mp-modal-bg");
            var box = new VisualElement(); box.AddToClassList("mp-modal"); box.AddToClassList("mp-langbox");
            var title = new Label(T("Dil seç", "Choose a language")); title.AddToClassList("mp-modal-title"); box.Add(title);
            var grid = new VisualElement(); grid.AddToClassList("mp-langgrid"); box.Add(grid);
            foreach (var l in Languages)
            {
                var lang = l;
                var b = new Button(() => { overlay.RemoveFromHierarchy(); Set(lang.Code); }) { text = lang.Native };
                b.AddToClassList("mp-btn"); b.AddToClassList("mp-langbtn");
                if (lang.Code == Code) b.AddToClassList("mp-btn-primary");
                // each name is shown in its own script, so the shaping generator is used where that script needs it
                if (lang.Complex) b.style.unityTextGenerator = UnityEngine.TextGeneratorType.Advanced;
                if (lang.Rtl) b.languageDirection = LanguageDirection.RTL;
                grid.Add(b);
            }
            var note = new Label(T("Türkçe ve İngilizce dışındaki çeviriler makine destekli hazırlanmıştır; hata görürseniz bildirin.",
                                   "Languages other than Turkish and English are machine-assisted translations; please report any errors."));
            note.AddToClassList("mp-note"); box.Add(note);
            var row = UIKit.ButtonRow(box);
            UIKit.Button(row, T("Kapat", "Close"), () => overlay.RemoveFromHierarchy());
            overlay.Add(box);
            overlay.RegisterCallback<PointerDownEvent>(e => { if (e.target == overlay) overlay.RemoveFromHierarchy(); });
            App.I.UIRoot.Add(overlay);
        }
    }
}
