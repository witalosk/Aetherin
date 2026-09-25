using System.Collections.Generic;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// RosettaUIによるポストエフェクト編集画面を管理する。
    /// 実際のレンダリング処理はPostEffectManager本体に残す。
    /// </summary>
    public sealed partial class PostEffectManager
    {
        // 0はVolume、1以降は編集中デッキのDeckインデックス + 1。
        private int _selectedEditorItem;
        private int _editorRevision;

        private PostEffectStack EditingStack => EditingDeck == StageDeck.Current ? _params.Current : _params.Next;
        private DeckVolumeEffects EditingVolume => EditingDeck == StageDeck.Current ? _params.CurrentVolume : _params.NextVolume;

        public Element AdditiveUi()
        {
            EnsureLutLibrary();
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();

            ClampSelectedEditorItem();
            return UI.Column(
                UI.Label(() => $"Editing: {EditingDeck}"),
                UI.Row(
                    UI.Box(UI.DynamicElementOnStatusChanged(
                        () => (_editorRevision, EditingDeck), _ => CreateEditorListElement())).SetWidth(190f).SetFlexShrink(0f),
                    UI.Box(UI.DynamicElementOnStatusChanged(
                        () => (_selectedEditorItem, EditingDeck, EditingStack.Decks.Count),
                        _ => CreateSelectedEditorElement())).SetMinWidth(420f).SetFlexGrow(1f)));
        }

        private Element CreateEditorListElement()
        {
            var items = new List<Element>
            {
                UI.Label(() => $"{EditingDeck} Editor"),
                UI.Button(UI.Label(() => $"{(_selectedEditorItem == 0 ? "▶ " : "  ")}Volume Effects"),
                    () => _selectedEditorItem = 0),
                UI.Button("+ Add Deck", AddDeck),
            };

            PostEffectStack stack = EditingStack;
            for (int i = 0; i < stack.Decks.Count; i++)
            {
                int deckIndex = i;
                items.Add(UI.Button(UI.Label(() =>
                    {
                        PostEffectDeck deck = EditingStack.Decks[deckIndex];
                        string name = string.IsNullOrWhiteSpace(deck?.Name) ? $"Deck {deckIndex + 1}" : deck.Name;
                        return $"{(_selectedEditorItem == deckIndex + 1 ? "▶ " : "  ")}{name}";
                    }),
                    () => _selectedEditorItem = deckIndex + 1));
            }

            return UI.Column(items);
        }

        private Element CreateSelectedEditorElement()
        {
            ClampSelectedEditorItem();
            if (_selectedEditorItem == 0)
            {
                return UI.Column(
                    UI.Label("URP Volume Effects"),
                    UI.Field(null, Binder.Create(EditingVolume, typeof(DeckVolumeEffects))));
            }

            int deckIndex = _selectedEditorItem - 1;
            PostEffectStack stack = EditingStack;
            PostEffectDeck deck = stack.Decks[deckIndex];
            deck ??= stack.Decks[deckIndex] = new PostEffectDeck();
            deck.EnsureInitialized();
            if (deck.Modules != null)
                foreach (PostEffectModule module in deck.Modules)
                    if (module != null)
                    {
                        module.GetAvailableLutKeys = GetLutKeys;
                        module.GetAvailableTextureKeys = GetTextureKeys;
                    }
            return UI.Column(
                UI.Row(
                    UI.Field("Name", () => deck.Name, value => deck.Name = value).SetFlexGrow(1f),
                    UI.Button("▲", () => MoveSelectedDeck(-1)).SetWidth(32f),
                    UI.Button("▼", () => MoveSelectedDeck(1)).SetWidth(32f),
                    UI.Button("Delete", DeleteSelectedDeck)),
                UI.Field(null, Binder.Create(deck, typeof(PostEffectDeck))));
        }

        private void AddDeck()
        {
            PostEffectStack stack = EditingStack;
            stack.Decks.Add(new PostEffectDeck { Name = $"Deck {stack.Decks.Count + 1}" });
            _selectedEditorItem = stack.Decks.Count;
            _editorRevision++;
        }

        private void MoveSelectedDeck(int direction)
        {
            int index = _selectedEditorItem - 1;
            int destination = index + direction;
            PostEffectStack stack = EditingStack;
            if (index < 0 || destination < 0 || destination >= stack.Decks.Count) return;
            PostEffectDeck deck = stack.Decks[index];
            stack.Decks.RemoveAt(index);
            stack.Decks.Insert(destination, deck);
            _selectedEditorItem = destination + 1;
            _editorRevision++;
        }

        private void DeleteSelectedDeck()
        {
            int index = _selectedEditorItem - 1;
            PostEffectStack stack = EditingStack;
            if (index < 0 || index >= stack.Decks.Count) return;
            stack.Decks.RemoveAt(index);
            _selectedEditorItem = 0;
            _editorRevision++;
        }

        private void ClampSelectedEditorItem()
        {
            EditingStack.Decks ??= new List<PostEffectDeck>();
            _selectedEditorItem = Mathf.Clamp(_selectedEditorItem, 0, EditingStack.Decks.Count);
        }
    }
}
