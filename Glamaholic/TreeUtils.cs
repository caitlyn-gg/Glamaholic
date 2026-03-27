using Glamaholic;
using System;
using System.Collections.Generic;
using System.Linq;

internal class TreeUtils
{
    public static TreeNode? FindNodeById(List<TreeNode> nodes, Guid id) {
        foreach (var node in nodes) {
            if (node.Id == id)
                return node;

            if (node is FolderNode folder) {
                var found = FindNodeById(folder.Children, id);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    public static (List<TreeNode>? parent, int index) FindNodeParent(List<TreeNode> rootNodes, Guid id) {
        for (int i = 0; i < rootNodes.Count; i++) {
            if (rootNodes[i].Id == id)
                return (rootNodes, i);
        }

        foreach (var node in rootNodes) {
            if (node is FolderNode folder) {
                var result = FindNodeParent(folder.Children, id);
                if (result.parent != null)
                    return result;
            }
        }

        return (null, -1);
    }

    public static bool MoveNodeToFolder(List<TreeNode> rootNodes, Guid nodeId, Guid? targetFolderId) {
        // Find the node to move
        var (sourceParent, sourceIndex) = FindNodeParent(rootNodes, nodeId);
        if (sourceParent == null || sourceIndex == -1)
            return false;

        var nodeToMove = sourceParent[sourceIndex];

        // Find target location
        List<TreeNode> targetParent;
        if (targetFolderId == null) {
            // Move to root
            targetParent = rootNodes;
        } else {
            var targetFolder = FindNodeById(rootNodes, targetFolderId.Value) as FolderNode;
            if (targetFolder == null)
                return false;
            targetParent = targetFolder.Children;
        }

        // Perform the move
        sourceParent.RemoveAt(sourceIndex);
        
        // Insert at appropriate position (folders first, then at end of plates section)
        int insertIndex = GetInsertIndexForNode(targetParent, nodeToMove);
        targetParent.Insert(insertIndex, nodeToMove);

        return true;
    }

    public static bool MoveNodeToPosition(List<TreeNode> rootNodes, Guid nodeId, Guid targetNodeId, bool insertAfter) {
        if (nodeId == targetNodeId)
            return false;

        // Find the node to move
        var (sourceParent, sourceIndex) = FindNodeParent(rootNodes, nodeId);
        if (sourceParent == null || sourceIndex == -1)
            return false;

        var nodeToMove = sourceParent[sourceIndex];

        // Find target position
        var (targetParent, targetIndex) = FindNodeParent(rootNodes, targetNodeId);
        if (targetParent == null || targetIndex == -1)
            return false;

        var targetNode = targetParent[targetIndex];

        // Prevent mixing folders and plates in invalid ways
        bool movingFolder = nodeToMove is FolderNode;
        bool targetIsFolder = targetNode is FolderNode;

        // Remove from source first
        sourceParent.RemoveAt(sourceIndex);

        // Recalculate target index if we removed from the same parent before the target
        if (sourceParent == targetParent && sourceIndex < targetIndex)
            targetIndex--;

        int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;

        // Keep folders at the top
        if (movingFolder) {
            // Folders can only be placed among other folders (at the top)
            int lastFolderIndex = GetLastFolderIndex(targetParent);
            if (insertIndex > lastFolderIndex + 1)
                insertIndex = lastFolderIndex + 1;
        } else {
            // Plates can only be placed among other plates (after folders)
            int firstPlateIndex = GetFirstPlateIndex(targetParent);
            if (insertIndex < firstPlateIndex)
                insertIndex = firstPlateIndex;
        }

        insertIndex = Math.Max(0, Math.Min(insertIndex, targetParent.Count));

        targetParent.Insert(insertIndex, nodeToMove);
        return true;
    }

    public static bool MoveNodeIntoFolder(List<TreeNode> rootNodes, Guid nodeId, Guid folderId) {
        if (nodeId == folderId)
            return false;

        // Find the node to move
        var (sourceParent, sourceIndex) = FindNodeParent(rootNodes, nodeId);
        if (sourceParent == null || sourceIndex == -1)
            return false;

        var nodeToMove = sourceParent[sourceIndex];

        // Find target folder
        var targetFolder = FindNodeById(rootNodes, folderId) as FolderNode;
        if (targetFolder == null)
            return false;

        // Remove from source
        sourceParent.RemoveAt(sourceIndex);

        int insertIndex = GetInsertIndexForNode(targetFolder.Children, nodeToMove);
        targetFolder.Children.Insert(insertIndex, nodeToMove);

        return true;
    }

    public static bool MoveNodeToRoot(List<TreeNode> rootNodes, Guid nodeId) {
        // Find the node to move
        var (sourceParent, sourceIndex) = FindNodeParent(rootNodes, nodeId);
        if (sourceParent == null || sourceIndex == -1)
            return false;

        // Already at root
        if (sourceParent == rootNodes)
            return false;

        var nodeToMove = sourceParent[sourceIndex];

        // Remove from source
        sourceParent.RemoveAt(sourceIndex);

        // Insert at appropriate position in root
        int insertIndex = GetInsertIndexForNode(rootNodes, nodeToMove);
        rootNodes.Insert(insertIndex, nodeToMove);

        return true;
    }

    private static int GetInsertIndexForNode(List<TreeNode> targetList, TreeNode nodeToInsert) {
        if (nodeToInsert is FolderNode) {
            // Insert at end of folders section
            return GetLastFolderIndex(targetList) + 1;
        } else {
            // Insert at end of list (after all folders and plates)
            return targetList.Count;
        }
    }

    private static int GetLastFolderIndex(List<TreeNode> nodes) {
        for (int i = nodes.Count - 1; i >= 0; i--) {
            if (nodes[i] is FolderNode)
                return i;
        }
        return -1;
    }

    private static int GetFirstPlateIndex(List<TreeNode> nodes) {
        for (int i = 0; i < nodes.Count; i++) {
            if (nodes[i] is PlateNode)
                return i;
        }
        return nodes.Count;
    }

    public static bool RemoveNode(List<TreeNode> rootNodes, Guid id) {
        var (parent, index) = FindNodeParent(rootNodes, id);
        if (parent == null || index == -1)
            return false;

        parent.RemoveAt(index);
        return true;
    }

    public static bool DeleteFolder(List<TreeNode> rootNodes, Guid folderId, bool moveContentsToParent = false) {
        var folder = FindNodeById(rootNodes, folderId) as FolderNode;
        if (folder == null)
            return false;

        var (parent, index) = FindNodeParent(rootNodes, folderId);
        if (parent == null || index == -1)
            return false;

        if (moveContentsToParent && folder.Children.Count > 0) {
            // Insert children at the folder's position
            parent.RemoveAt(index);
            parent.InsertRange(index, folder.Children);
        } else {
            // Just remove the folder (and all contents)
            parent.RemoveAt(index);
        }

        return true;
    }

    public static List<(Guid id, PlateNode plate)> GetAllPlates(List<TreeNode> rootNodes) {
        var plates = new List<(Guid, PlateNode)>();
        CollectPlates(rootNodes, plates);
        return plates;
    }

    private static void CollectPlates(List<TreeNode> nodes, List<(Guid, PlateNode)> plates) {
        foreach (var node in nodes) {
            if (node is PlateNode plate) {
                plates.Add((node.Id, plate));
            } else if (node is FolderNode folder) {
                CollectPlates(folder.Children, plates);
            }
        }
    }

    public static List<(Guid id, string path)> GetAllFolders(List<TreeNode> rootNodes) {
        var folders = new List<(Guid, string)>();
        CollectFolders(rootNodes, "", folders);
        return folders;
    }

    private static void CollectFolders(List<TreeNode> nodes, string parentPath, List<(Guid, string)> folders) {
        foreach (var node in nodes) {
            if (node is FolderNode folder) {
                var path = string.IsNullOrEmpty(parentPath) ? folder.Name : $"{parentPath}/{folder.Name}";
                folders.Add((node.Id, path));
                CollectFolders(folder.Children, path, folders);
            }
        }
    }

    public static bool ReplacePlate(List<TreeNode> rootNodes, Guid plateId, SavedPlate newPlate) {
        var (parent, index) = FindNodeParent(rootNodes, plateId);
        if (parent == null || index == -1 || parent[index] is not PlateNode)
            return false;

        var newNode = new PlateNode(newPlate) { Id = plateId }; // Preserve the ID
        parent[index] = newNode;
        return true;
    }

    public static int GetPlateIndex(List<TreeNode> rootNodes, Guid plateId) {
        var plates = GetAllPlates(rootNodes);
        for (int i = 0; i < plates.Count; i++) {
            if (plates[i].id == plateId)
                return i;
        }
        return -1;
    }

    public static Guid? GetPlateIdByIndex(List<TreeNode> rootNodes, int index) {
        var plates = GetAllPlates(rootNodes);
        if (index >= 0 && index < plates.Count)
            return plates[index].id;
        return null;
    }

    public static void EnsureFoldersFirst(List<TreeNode> rootNodes)
    {
        // Separate folders and plates and maintain local order
        var folders = rootNodes.Where(n => n is FolderNode).ToList();
        var plates = rootNodes.Where(n => n is PlateNode).ToList();
        
        rootNodes.Clear();
        rootNodes.AddRange(folders);
        rootNodes.AddRange(plates);

        foreach (var node in rootNodes)
        {
            if (node is FolderNode folder)
                EnsureFoldersFirst(folder.Children);
        }
    }

    public static void Sort(List<TreeNode> rootNodes)
    {
        EnsureFoldersFirst(rootNodes);
    }
}