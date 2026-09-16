using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RosettaUI.UIToolkit
{
    /// <summary>Hosts edge, split, and tab-docked RosettaUI windows.</summary>
    public sealed class DockingWorkspace : VisualElement
    {
        private const float EdgeSize = 42f;
        private const float SplitRatio = 0.5f;
        private const float MinimumPaneSize = 100f;
        private static readonly Dictionary<VisualElement, DockingWorkspace> Workspaces = new();
        private readonly List<DockingGroup> _groups = new();
        private readonly List<DockingDivider> _dividers = new();
        private readonly List<EdgeResizeHandle> _edgeHandles = new();
        private readonly List<BoundaryLink> _boundaryLinks = new();
        private readonly VisualElement _preview = new();

        private DockingWorkspace()
        {
            name = "rosettaui-docking-workspace";
            pickingMode = PickingMode.Ignore;
            AddToClassList("rosettaui-docking-workspace");
            style.position = Position.Absolute;
            style.left = 0;
            style.right = 0;
            style.top = 0;
            style.bottom = 0;

            _preview.pickingMode = PickingMode.Ignore;
            _preview.AddToClassList("rosettaui-docking-preview");
            _preview.style.position = Position.Absolute;
            _preview.style.display = DisplayStyle.None;
            hierarchy.Add(_preview);
        }

        public static DockingWorkspace For(VisualElement root)
        {
            if (root == null) return null;
            if (Workspaces.TryGetValue(root, out var workspace)) return workspace;
            workspace = new DockingWorkspace();
            Workspaces.Add(root, workspace);
            root.Insert(0, workspace);
            return workspace;
        }

        public void UpdateDockPreview(Vector2 worldPosition)
        {
            if (!TryGetDockProposal(worldPosition, out _, out _, out var rect))
            {
                HideDockPreview();
                return;
            }
            SetAbsoluteRect(_preview, rect);
            _preview.style.display = DisplayStyle.Flex;
            _preview.BringToFront();
        }

        public void HideDockPreview() => _preview.style.display = DisplayStyle.None;

        public void TryDock(Window window, Vector2 worldPosition)
        {
            HideDockPreview();
            if (window == null || !TryGetDockProposal(worldPosition, out var target, out var side, out var rect)) return;
            if (target == null)
            {
                var group = AddGroup(window, rect);
                AddEdgeResizeHandle(group, Opposite(side));
                return;
            }
            if (side == DockSide.None)
            {
                target.AddWindow(window);
                return;
            }
            var split = Split(target.Rect, side);
            RemoveBoundaryLinks(target, side);
            target.SetRect(split.remaining, false);
            var added = AddGroup(window, split.added);
            AddDivider(added, target, side);
        }

        private bool TryGetDockProposal(Vector2 worldPosition, out DockingGroup target, out DockSide side, out Rect rect)
        {
            target = null;
            side = DockSide.None;
            rect = default;
            if (panel == null || !worldBound.Contains(worldPosition)) return false;
            var local = ToLocal(worldPosition);
            target = FindGroup(local);
            if (target != null)
            {
                side = EdgeAt(local, target.Rect);
                rect = side == DockSide.None ? target.Rect : Split(target.Rect, side).added;
                return true;
            }
            var bounds = contentRect;
            if (local.x > EdgeSize && local.x < bounds.width - EdgeSize &&
                local.y > EdgeSize && local.y < bounds.height - EdgeSize) return false;
            side = ClosestSide(local, bounds);
            var availableEdgeBounds = GetAvailableEdgeBounds(bounds, side, local);
            if (availableEdgeBounds.width < MinimumPaneSize || availableEdgeBounds.height < MinimumPaneSize)
                return false;
            rect = EdgeRect(availableEdgeBounds, side, 0.3f);
            return true;
        }

        /// <summary>
        /// Returns the contiguous, unoccupied part of a screen edge containing the pointer.
        /// A left pane that already reaches the bottom edge, for example, cuts that occupied
        /// interval out so a new bottom pane begins at the left pane's right boundary.
        /// </summary>
        private Rect GetAvailableEdgeBounds(Rect bounds, DockSide side, Vector2 point)
        {
            const float tolerance = 1f;
            var horizontalEdge = side is DockSide.Top or DockSide.Bottom;
            var axisPosition = horizontalEdge ? point.x : point.y;
            var start = horizontalEdge ? bounds.xMin : bounds.yMin;
            var end = horizontalEdge ? bounds.xMax : bounds.yMax;

            foreach (var group in _groups)
            {
                var rect = group.Rect;
                var touchesEdge = side switch
                {
                    DockSide.Left => Mathf.Abs(rect.xMin - bounds.xMin) <= tolerance,
                    DockSide.Right => Mathf.Abs(rect.xMax - bounds.xMax) <= tolerance,
                    DockSide.Top => Mathf.Abs(rect.yMin - bounds.yMin) <= tolerance,
                    DockSide.Bottom => Mathf.Abs(rect.yMax - bounds.yMax) <= tolerance,
                    _ => false
                };
                if (!touchesEdge) continue;

                var intervalStart = horizontalEdge ? rect.xMin : rect.yMin;
                var intervalEnd = horizontalEdge ? rect.xMax : rect.yMax;
                if (intervalEnd <= axisPosition + tolerance)
                    start = Mathf.Max(start, intervalEnd);
                else if (intervalStart >= axisPosition - tolerance)
                    end = Mathf.Min(end, intervalStart);
            }

            return horizontalEdge
                ? new Rect(start, bounds.yMin, Mathf.Max(0f, end - start), bounds.height)
                : new Rect(bounds.xMin, start, bounds.width, Mathf.Max(0f, end - start));
        }

        private DockingGroup AddGroup(Window window, Rect rect)
        {
            var group = new DockingGroup(this);
            _groups.Add(group);
            hierarchy.Add(group);
            group.SetRect(rect);
            RegisterAdjacencies(group);
            group.AddWindow(window);
            return group;
        }

        private void SetGroupRect(DockingGroup group, Rect rect, bool propagate)
        {
            var oldRect = group.Rect;
            if (Approximately(oldRect, rect)) return;
            group.SetRectDirect(rect);
            if (propagate) PropagateBoundaryChange(group, oldRect, rect);
            RefreshHandles();
        }

        private void PropagateBoundaryChange(DockingGroup source, Rect oldRect, Rect newRect)
        {
            var queue = new Queue<(DockingGroup group, Rect oldRect, Rect newRect)>();
            queue.Enqueue((source, oldRect, newRect));
            var remainingIterations = Mathf.Max(8, _boundaryLinks.Count * 4);

            while (queue.Count > 0 && remainingIterations-- > 0)
            {
                var change = queue.Dequeue();
                foreach (var link in _boundaryLinks)
                {
                    if (!link.TryGetOther(change.group, out var other, out var sourceSide, out var otherSide)) continue;
                    var oldBoundary = GetBoundary(change.oldRect, sourceSide);
                    var newBoundary = GetBoundary(change.newRect, sourceSide);
                    if (Mathf.Abs(oldBoundary - newBoundary) <= 0.01f) continue;

                    var otherOldRect = other.Rect;
                    var otherNewRect = SetBoundary(otherOldRect, otherSide, newBoundary);
                    if (Approximately(otherOldRect, otherNewRect)) continue;
                    other.SetRectDirect(otherNewRect);
                    queue.Enqueue((other, otherOldRect, otherNewRect));
                }
            }
        }

        private void RegisterAdjacencies(DockingGroup group)
        {
            foreach (var other in _groups)
            {
                if (other == group) continue;
                TryAddBoundaryLink(group, DockSide.Right, other, DockSide.Left, VerticalOverlap(group.Rect, other.Rect));
                TryAddBoundaryLink(group, DockSide.Left, other, DockSide.Right, VerticalOverlap(group.Rect, other.Rect));
                TryAddBoundaryLink(group, DockSide.Bottom, other, DockSide.Top, HorizontalOverlap(group.Rect, other.Rect));
                TryAddBoundaryLink(group, DockSide.Top, other, DockSide.Bottom, HorizontalOverlap(group.Rect, other.Rect));
            }
        }

        private void TryAddBoundaryLink(DockingGroup a, DockSide aSide, DockingGroup b, DockSide bSide, float overlap)
        {
            if (overlap <= 1f || Mathf.Abs(GetBoundary(a.Rect, aSide) - GetBoundary(b.Rect, bSide)) > 1f) return;
            foreach (var link in _boundaryLinks)
                if (link.Matches(a, aSide, b, bSide)) return;
            _boundaryLinks.Add(new BoundaryLink(a, aSide, b, bSide));
        }

        private void RemoveBoundaryLinks(DockingGroup group, DockSide side)
        {
            _boundaryLinks.RemoveAll(link => link.Contains(group, side));
        }

        private void RebuildBoundaryLinks()
        {
            _boundaryLinks.Clear();
            foreach (var group in _groups) RegisterAdjacencies(group);
        }

        private void AddDivider(DockingGroup added, DockingGroup remaining, DockSide addedSide)
        {
            var divider = new DockingDivider(this, added, remaining, addedSide);
            _dividers.Add(divider);
            hierarchy.Add(divider);
            divider.Refresh();
        }

        private void AddEdgeResizeHandle(DockingGroup group, DockSide resizeEdge)
        {
            var handle = new EdgeResizeHandle(this, group, resizeEdge);
            _edgeHandles.Add(handle);
            hierarchy.Add(handle);
            handle.Refresh();
        }

        private void Undock(Window window, DockingGroup group, Vector2 worldPosition)
        {
            var size = window.layout.size;
            if (!group.RemoveWindow(window)) return;
            if (group.IsEmpty) RemoveGroup(group);
            window.Undock(parent, worldPosition, size);
        }

        private void RemoveGroup(DockingGroup group)
        {
            _groups.Remove(group);
            _boundaryLinks.RemoveAll(link => link.Contains(group));
            for (var i = _dividers.Count - 1; i >= 0; --i)
            {
                var divider = _dividers[i];
                if (!divider.Contains(group)) continue;
                var other = divider.Other(group);
                if (other != null && _groups.Contains(other)) other.SetRect(Union(group.Rect, other.Rect), false);
                divider.RemoveFromHierarchy();
                _dividers.RemoveAt(i);
            }
            for (var i = _edgeHandles.Count - 1; i >= 0; --i)
            {
                if (_edgeHandles[i].Group != group) continue;
                _edgeHandles[i].RemoveFromHierarchy();
                _edgeHandles.RemoveAt(i);
            }
            group.RemoveFromHierarchy();
            RebuildBoundaryLinks();
            RefreshHandles();
        }

        private void RefreshHandles()
        {
            foreach (var divider in _dividers) divider.Refresh();
            foreach (var handle in _edgeHandles) handle.Refresh();
        }

        private DockingGroup FindGroup(Vector2 local)
        {
            for (var i = _groups.Count - 1; i >= 0; --i)
                if (_groups[i].Rect.Contains(local)) return _groups[i];
            return null;
        }

        private Vector2 ToLocal(Vector2 worldPosition) => worldPosition - worldBound.position;

        private static DockSide EdgeAt(Vector2 point, Rect rect)
        {
            const float zone = 28f;
            if (!rect.Contains(point)) return DockSide.None;
            if (point.x - rect.xMin < zone) return DockSide.Left;
            if (rect.xMax - point.x < zone) return DockSide.Right;
            if (point.y - rect.yMin < zone) return DockSide.Top;
            if (rect.yMax - point.y < zone) return DockSide.Bottom;
            return DockSide.None;
        }

        private static DockSide ClosestSide(Vector2 point, Rect rect)
        {
            var distances = new[]
            {
                point.x - rect.xMin,
                rect.xMax - point.x,
                point.y - rect.yMin,
                rect.yMax - point.y
            };
            var min = 0;
            for (var i = 1; i < distances.Length; ++i) if (distances[i] < distances[min]) min = i;
            return (DockSide)(min + 1);
        }

        private static DockSide Opposite(DockSide side) => side switch
        {
            DockSide.Left => DockSide.Right, DockSide.Right => DockSide.Left,
            DockSide.Top => DockSide.Bottom, DockSide.Bottom => DockSide.Top, _ => DockSide.None
        };

        private static Rect EdgeRect(Rect bounds, DockSide side, float ratio) => side switch
        {
            DockSide.Left => new Rect(bounds.xMin, bounds.yMin, bounds.width * ratio, bounds.height),
            DockSide.Right => new Rect(bounds.xMax - bounds.width * ratio, bounds.yMin, bounds.width * ratio, bounds.height),
            DockSide.Top => new Rect(bounds.xMin, bounds.yMin, bounds.width, bounds.height * ratio),
            _ => new Rect(bounds.xMin, bounds.yMax - bounds.height * ratio, bounds.width, bounds.height * ratio)
        };

        private static (Rect remaining, Rect added) Split(Rect rect, DockSide side)
        {
            var amount = (side is DockSide.Left or DockSide.Right ? rect.width : rect.height) * SplitRatio;
            return side switch
            {
                DockSide.Left => (new Rect(rect.x + amount, rect.y, rect.width - amount, rect.height), new Rect(rect.x, rect.y, amount, rect.height)),
                DockSide.Right => (new Rect(rect.x, rect.y, rect.width - amount, rect.height), new Rect(rect.x + rect.width - amount, rect.y, amount, rect.height)),
                DockSide.Top => (new Rect(rect.x, rect.y + amount, rect.width, rect.height - amount), new Rect(rect.x, rect.y, rect.width, amount)),
                _ => (new Rect(rect.x, rect.y, rect.width, rect.height - amount), new Rect(rect.x, rect.y + rect.height - amount, rect.width, amount))
            };
        }

        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        private static float HorizontalOverlap(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        private static float VerticalOverlap(Rect a, Rect b) => Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);

        private static float GetBoundary(Rect rect, DockSide side) => side switch
        {
            DockSide.Left => rect.xMin,
            DockSide.Right => rect.xMax,
            DockSide.Top => rect.yMin,
            DockSide.Bottom => rect.yMax,
            _ => 0f
        };

        private static Rect SetBoundary(Rect rect, DockSide side, float value)
        {
            switch (side)
            {
                case DockSide.Left: rect.xMin = Mathf.Min(value, rect.xMax - MinimumPaneSize); break;
                case DockSide.Right: rect.xMax = Mathf.Max(value, rect.xMin + MinimumPaneSize); break;
                case DockSide.Top: rect.yMin = Mathf.Min(value, rect.yMax - MinimumPaneSize); break;
                case DockSide.Bottom: rect.yMax = Mathf.Max(value, rect.yMin + MinimumPaneSize); break;
            }
            return rect;
        }

        private static bool Approximately(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) <= 0.01f && Mathf.Abs(a.y - b.y) <= 0.01f &&
            Mathf.Abs(a.width - b.width) <= 0.01f && Mathf.Abs(a.height - b.height) <= 0.01f;

        private static void SetAbsoluteRect(VisualElement element, Rect rect)
        {
            element.style.left = rect.x; element.style.top = rect.y;
            element.style.width = rect.width; element.style.height = rect.height;
        }

        private enum DockSide { None, Left, Right, Top, Bottom }

        private sealed class BoundaryLink
        {
            private readonly DockingGroup _a;
            private readonly DockSide _aSide;
            private readonly DockingGroup _b;
            private readonly DockSide _bSide;

            public BoundaryLink(DockingGroup a, DockSide aSide, DockingGroup b, DockSide bSide)
            {
                _a = a; _aSide = aSide; _b = b; _bSide = bSide;
            }

            public bool TryGetOther(DockingGroup group, out DockingGroup other, out DockSide sourceSide, out DockSide otherSide)
            {
                if (group == _a) { other = _b; sourceSide = _aSide; otherSide = _bSide; return true; }
                if (group == _b) { other = _a; sourceSide = _bSide; otherSide = _aSide; return true; }
                other = null; sourceSide = DockSide.None; otherSide = DockSide.None; return false;
            }

            public bool Matches(DockingGroup a, DockSide aSide, DockingGroup b, DockSide bSide) =>
                (_a == a && _aSide == aSide && _b == b && _bSide == bSide) ||
                (_a == b && _aSide == bSide && _b == a && _bSide == aSide);

            public bool Contains(DockingGroup group) => _a == group || _b == group;
            public bool Contains(DockingGroup group, DockSide side) =>
                (_a == group && _aSide == side) || (_b == group && _bSide == side);
        }

        private sealed class DockingGroup : VisualElement
        {
            private readonly DockingWorkspace _workspace;
            private readonly Tabs _tabs = new();
            public Rect Rect { get; private set; }

            public DockingGroup(DockingWorkspace workspace)
            {
                _workspace = workspace;
                pickingMode = PickingMode.Position;
                AddToClassList("rosettaui-docking-group");
                style.position = Position.Absolute;
                style.flexDirection = FlexDirection.Column;
                hierarchy.Add(_tabs);
                _tabs.style.flexGrow = 1f;
            }

            public void SetRect(Rect rect, bool propagate = true)
            {
                _workspace.SetGroupRect(this, rect, propagate);
            }

            public void SetRectDirect(Rect rect)
            {
                Rect = rect;
                SetAbsoluteRect(this, rect);
            }

            public void AddWindow(Window window)
            {
                window.SetDocked(true);
                var tabHeader = new VisualElement { pickingMode = PickingMode.Position };
                tabHeader.AddToClassList("rosettaui-docking-tab-header");
                var title = new Label(string.IsNullOrEmpty(window.DockTitle) ? "Window" : window.DockTitle) { pickingMode = PickingMode.Ignore };
                title.AddToClassList("rosettaui-docking-tab-title");
                tabHeader.Add(title);
                var floatButton = new Label("↗") { tooltip = "フローティングウィンドウに戻す", pickingMode = PickingMode.Position };
                floatButton.AddToClassList("rosettaui-docking-tab-float-button");
                tabHeader.Add(floatButton);
                _tabs.AddTab(tabHeader, window);

                Vector2 dragStart = default, dragPosition = default;
                var wasDragged = false;
                tabHeader.AddManipulator(new DragManipulator(
                    evt =>
                    {
                        if (evt.button != 0) return false;
                        evt.StopPropagation();
                        dragStart = dragPosition = evt.position;
                        wasDragged = false;
                        return true;
                    },
                    evt =>
                    {
                        dragPosition = evt.position;
                        wasDragged |= Vector2.SqrMagnitude(dragPosition - dragStart) >= 100f;
                    },
                    _ =>
                    {
                        if (wasDragged) _workspace.Undock(window, this, dragPosition);
                        else _tabs.SelectTab(window);
                    }));
                floatButton.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    evt.StopPropagation();
                    _workspace.Undock(window, this, evt.position);
                });
                window.Show();
            }

            public bool IsEmpty => _tabs.TabCount == 0;
            public bool RemoveWindow(Window window) => _tabs.RemoveTab(window);
        }

        private sealed class DockingDivider : VisualElement
        {
            private const float Thickness = 7f;
            private readonly DockingWorkspace _workspace;
            private readonly DockSide _addedSide;
            public readonly DockingGroup Added;
            public readonly DockingGroup Remaining;

            public DockingDivider(DockingWorkspace workspace, DockingGroup added, DockingGroup remaining, DockSide addedSide)
            {
                _workspace = workspace; Added = added; Remaining = remaining; _addedSide = addedSide;
                pickingMode = PickingMode.Position; style.position = Position.Absolute;
                AddToClassList("rosettaui-docking-divider");
                AddToClassList(addedSide is DockSide.Left or DockSide.Right ? "rosettaui-docking-divider--vertical" : "rosettaui-docking-divider--horizontal");
                this.AddManipulator(new DragManipulator(null, evt => Resize(evt.position), _ => Refresh()));
            }

            public bool Contains(DockingGroup group) => Added == group || Remaining == group;
            public DockingGroup Other(DockingGroup group) => Added == group ? Remaining : Remaining == group ? Added : null;

            public void Refresh()
            {
                var a = Added.Rect; var b = Remaining.Rect;
                if (_addedSide is DockSide.Left or DockSide.Right)
                {
                    var x = _addedSide == DockSide.Left ? a.xMax : b.xMax;
                    SetAbsoluteRect(this, new Rect(x - Thickness * .5f, Mathf.Min(a.yMin, b.yMin), Thickness, Mathf.Max(a.yMax, b.yMax) - Mathf.Min(a.yMin, b.yMin)));
                }
                else
                {
                    var y = _addedSide == DockSide.Top ? a.yMax : b.yMax;
                    SetAbsoluteRect(this, new Rect(Mathf.Min(a.xMin, b.xMin), y - Thickness * .5f, Mathf.Max(a.xMax, b.xMax) - Mathf.Min(a.xMin, b.xMin), Thickness));
                }
                BringToFront();
            }

            private void Resize(Vector2 worldPosition)
            {
                var p = _workspace.ToLocal(worldPosition); var union = Union(Added.Rect, Remaining.Rect);
                if (_addedSide is DockSide.Left or DockSide.Right)
                {
                    var x = Mathf.Clamp(p.x, union.xMin + MinimumPaneSize, union.xMax - MinimumPaneSize);
                    var left = new Rect(union.xMin, union.yMin, x - union.xMin, union.height);
                    var right = new Rect(x, union.yMin, union.xMax - x, union.height);
                    if (_addedSide == DockSide.Left) { Added.SetRect(left); Remaining.SetRect(right); }
                    else { Remaining.SetRect(left); Added.SetRect(right); }
                }
                else
                {
                    var y = Mathf.Clamp(p.y, union.yMin + MinimumPaneSize, union.yMax - MinimumPaneSize);
                    var top = new Rect(union.xMin, union.yMin, union.width, y - union.yMin);
                    var bottom = new Rect(union.xMin, y, union.width, union.yMax - y);
                    if (_addedSide == DockSide.Top) { Added.SetRect(top); Remaining.SetRect(bottom); }
                    else { Remaining.SetRect(top); Added.SetRect(bottom); }
                }
                Refresh();
            }
        }

        private sealed class EdgeResizeHandle : VisualElement
        {
            private const float Thickness = 7f;
            private readonly DockingWorkspace _workspace;
            private readonly DockSide _edge;
            public DockingGroup Group { get; }

            public EdgeResizeHandle(DockingWorkspace workspace, DockingGroup group, DockSide edge)
            {
                _workspace = workspace; Group = group; _edge = edge;
                pickingMode = PickingMode.Position; style.position = Position.Absolute;
                AddToClassList("rosettaui-docking-divider");
                AddToClassList(edge is DockSide.Left or DockSide.Right ? "rosettaui-docking-divider--vertical" : "rosettaui-docking-divider--horizontal");
                this.AddManipulator(new DragManipulator(null, evt => Resize(evt.position), _ => Refresh()));
            }

            public void Refresh()
            {
                var rect = Group.Rect;
                if (_edge is DockSide.Left or DockSide.Right)
                {
                    var x = _edge == DockSide.Left ? rect.xMin : rect.xMax;
                    SetAbsoluteRect(this, new Rect(x - Thickness * .5f, rect.yMin, Thickness, rect.height));
                }
                else
                {
                    var y = _edge == DockSide.Top ? rect.yMin : rect.yMax;
                    SetAbsoluteRect(this, new Rect(rect.xMin, y - Thickness * .5f, rect.width, Thickness));
                }
                BringToFront();
            }

            private void Resize(Vector2 worldPosition)
            {
                var p = _workspace.ToLocal(worldPosition); var rect = Group.Rect; var bounds = _workspace.contentRect;
                switch (_edge)
                {
                    case DockSide.Left: rect.xMin = Mathf.Clamp(p.x, 0, rect.xMax - MinimumPaneSize); break;
                    case DockSide.Right: rect.xMax = Mathf.Clamp(p.x, rect.xMin + MinimumPaneSize, bounds.width); break;
                    case DockSide.Top: rect.yMin = Mathf.Clamp(p.y, 0, rect.yMax - MinimumPaneSize); break;
                    case DockSide.Bottom: rect.yMax = Mathf.Clamp(p.y, rect.yMin + MinimumPaneSize, bounds.height); break;
                }
                Group.SetRect(rect); Refresh();
            }
        }
    }
}
