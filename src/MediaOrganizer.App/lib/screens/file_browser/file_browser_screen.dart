import 'package:flutter/material.dart';
import '../../services/api_service.dart';

class FileBrowserScreen extends StatefulWidget {
  final ApiService api;

  const FileBrowserScreen({super.key, required this.api});

  @override
  State<FileBrowserScreen> createState() => _FileBrowserScreenState();
}

class _FileBrowserScreenState extends State<FileBrowserScreen> {
  late Future<Map<String, dynamic>> _browseFuture;
  String _currentPath = '';
  final List<String> _pathHistory = [];
  final Set<String> _selectedPaths = {};

  bool get _isSelectionMode => _selectedPaths.isNotEmpty;

  @override
  void initState() {
    super.initState();
    _browseFuture = widget.api.browse();
  }

  void _navigateTo(String path) {
    _pathHistory.add(_currentPath);
    setState(() {
      _currentPath = path;
      _selectedPaths.clear();
      _browseFuture = widget.api.browse(path: path);
    });
  }

  void _navigateBack() {
    if (_pathHistory.isEmpty) return;
    final previous = _pathHistory.removeLast();
    setState(() {
      _currentPath = previous;
      _selectedPaths.clear();
      _browseFuture = widget.api.browse(path: previous);
    });
  }

  void _refresh() {
    setState(() {
      _selectedPaths.clear();
      _browseFuture = widget.api.browse(
        path: _currentPath.isEmpty ? null : _currentPath,
      );
    });
  }

  void _toggleSelection(String path) {
    setState(() {
      if (_selectedPaths.contains(path)) {
        _selectedPaths.remove(path);
      } else {
        _selectedPaths.add(path);
      }
    });
  }

  void _clearSelection() {
    setState(() => _selectedPaths.clear());
  }

  Future<void> _deleteSelected() async {
    final count = _selectedPaths.length;
    final confirmed = await showDialog<bool>(
      context: context,
      builder:
          (ctx) => AlertDialog(
            title: const Text('Delete selected?'),
            content: Text(
              'Permanently delete $count item${count != 1 ? 's' : ''}? This cannot be undone.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(ctx).pop(false),
                child: const Text('Cancel'),
              ),
              FilledButton(
                style: FilledButton.styleFrom(
                  backgroundColor: Theme.of(ctx).colorScheme.error,
                ),
                onPressed: () => Navigator.of(ctx).pop(true),
                child: const Text('Delete'),
              ),
            ],
          ),
    );

    if (confirmed != true) return;

    try {
      final result = await widget.api.deleteItems(
        paths: _selectedPaths.toList(),
      );
      _refresh();
      if (mounted) {
        final deleted = result['deletedCount'] ?? 0;
        final errors = result['errors'] as List<dynamic>? ?? [];
        var msg = 'Deleted $deleted item${deleted != 1 ? 's' : ''}.';
        if (errors.isNotEmpty) {
          msg += ' ${errors.length} error${errors.length != 1 ? 's' : ''}.';
        }
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(msg)));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Delete failed: $e')));
      }
    }
  }

  Future<void> _showRenameDialog(String path, String currentName) async {
    final controller = TextEditingController(text: currentName);
    final newName = await showDialog<String>(
      context: context,
      builder:
          (ctx) => AlertDialog(
            title: const Text('Rename'),
            content: TextField(
              controller: controller,
              autofocus: true,
              decoration: const InputDecoration(labelText: 'New name'),
              onSubmitted: (value) => Navigator.of(ctx).pop(value),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(ctx).pop(),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.of(ctx).pop(controller.text),
                child: const Text('Rename'),
              ),
            ],
          ),
    );

    if (newName == null || newName.isEmpty || newName == currentName) return;

    try {
      await widget.api.rename(path: path, newName: newName);
      _refresh();
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Renamed successfully.')));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Rename failed: $e')));
      }
    }
  }

  Future<void> _confirmDeleteSingle(String path, String name) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder:
          (ctx) => AlertDialog(
            title: const Text('Delete?'),
            content: Text('Permanently delete "$name"? This cannot be undone.'),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(ctx).pop(false),
                child: const Text('Cancel'),
              ),
              FilledButton(
                style: FilledButton.styleFrom(
                  backgroundColor: Theme.of(ctx).colorScheme.error,
                ),
                onPressed: () => Navigator.of(ctx).pop(true),
                child: const Text('Delete'),
              ),
            ],
          ),
    );

    if (confirmed != true) return;

    try {
      await widget.api.deleteItems(paths: [path]);
      _refresh();
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Deleted successfully.')));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Delete failed: $e')));
      }
    }
  }

  Future<void> _moveItem(String sourcePath, String destinationFolder) async {
    try {
      await widget.api.moveItem(
        sourcePath: sourcePath,
        destinationFolder: destinationFolder,
      );
      _refresh();
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Moved successfully.')));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Move failed: $e')));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar:
          _isSelectionMode
              ? AppBar(
                leading: IconButton(
                  icon: const Icon(Icons.close),
                  onPressed: _clearSelection,
                ),
                title: Text('${_selectedPaths.length} selected'),
                actions: [
                  IconButton(
                    icon: const Icon(Icons.delete),
                    tooltip: 'Delete selected',
                    onPressed: _deleteSelected,
                  ),
                ],
              )
              : AppBar(
                leading:
                    _currentPath.isNotEmpty
                        ? IconButton(
                          icon: const Icon(Icons.arrow_back),
                          onPressed: _navigateBack,
                        )
                        : null,
                title: Text(
                  _currentPath.isEmpty ? 'Source Files' : _currentPath,
                  overflow: TextOverflow.ellipsis,
                ),
                actions: [
                  IconButton(
                    icon: const Icon(Icons.refresh),
                    tooltip: 'Refresh',
                    onPressed: _refresh,
                  ),
                ],
              ),
      body: FutureBuilder<Map<String, dynamic>>(
        future: _browseFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }

          if (snapshot.hasError) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(32),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      Icons.error_outline,
                      size: 48,
                      color: Theme.of(context).colorScheme.error,
                    ),
                    const SizedBox(height: 16),
                    Text(
                      'Failed to load directory:\n${snapshot.error}',
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 16),
                    FilledButton.icon(
                      onPressed: _refresh,
                      icon: const Icon(Icons.refresh),
                      label: const Text('Retry'),
                    ),
                  ],
                ),
              ),
            );
          }

          final data = snapshot.data!;
          final directories = data['directories'] as List<dynamic>? ?? [];
          final files = data['files'] as List<dynamic>? ?? [];

          if (directories.isEmpty && files.isEmpty) {
            return const Center(child: Text('This folder is empty.'));
          }

          return _BrowseList(
            currentPath: _currentPath,
            directories: directories,
            files: files,
            selectedPaths: _selectedPaths,
            isSelectionMode: _isSelectionMode,
            onNavigate: _navigateTo,
            onToggleSelection: _toggleSelection,
            onRename: _showRenameDialog,
            onMove: _moveItem,
            onDelete: _confirmDeleteSingle,
          );
        },
      ),
    );
  }
}

class _BrowseList extends StatelessWidget {
  final String currentPath;
  final List<dynamic> directories;
  final List<dynamic> files;
  final Set<String> selectedPaths;
  final bool isSelectionMode;
  final void Function(String path) onNavigate;
  final void Function(String path) onToggleSelection;
  final Future<void> Function(String path, String currentName) onRename;
  final Future<void> Function(String sourcePath, String destFolder) onMove;
  final Future<void> Function(String path, String name) onDelete;

  const _BrowseList({
    required this.currentPath,
    required this.directories,
    required this.files,
    required this.selectedPaths,
    required this.isSelectionMode,
    required this.onNavigate,
    required this.onToggleSelection,
    required this.onRename,
    required this.onMove,
    required this.onDelete,
  });

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.symmetric(vertical: 8),
      children: [
        ...directories.map((dir) {
          final name = dir['name'] as String? ?? '';
          final path = dir['path'] as String? ?? '';
          final isSelected = selectedPaths.contains(path);

          return DragTarget<String>(
            onWillAcceptWithDetails: (details) => details.data != path,
            onAcceptWithDetails: (details) => onMove(details.data, path),
            builder: (context, candidateData, rejectedData) {
              final isHovering = candidateData.isNotEmpty;
              return LongPressDraggable<String>(
                data: path,
                feedback: Material(
                  elevation: 4,
                  borderRadius: BorderRadius.circular(8),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 8,
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(Icons.folder, size: 20),
                        const SizedBox(width: 8),
                        Text(name),
                      ],
                    ),
                  ),
                ),
                childWhenDragging: Opacity(
                  opacity: 0.5,
                  child: _buildDirTile(name, path, isSelected, false),
                ),
                child: _buildDirTile(name, path, isSelected, isHovering),
              );
            },
          );
        }),
        ...files.map((file) {
          final name = file['name'] as String? ?? '';
          final path = file['path'] as String? ?? '';
          final size = file['size'] as int? ?? 0;
          final isSelected = selectedPaths.contains(path);

          return LongPressDraggable<String>(
            data: path,
            feedback: Material(
              elevation: 4,
              borderRadius: BorderRadius.circular(8),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 8,
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.insert_drive_file_outlined, size: 20),
                    const SizedBox(width: 8),
                    Text(name),
                  ],
                ),
              ),
            ),
            childWhenDragging: Opacity(
              opacity: 0.5,
              child: _buildFileTile(name, path, size, isSelected),
            ),
            child: _buildFileTile(name, path, size, isSelected),
          );
        }),
      ],
    );
  }

  Widget _buildDirTile(
    String name,
    String path,
    bool isSelected,
    bool isHovering,
  ) {
    return ListTile(
      tileColor: isHovering ? Colors.blue.withAlpha(30) : null,
      leading:
          isSelectionMode
              ? Checkbox(
                value: isSelected,
                onChanged: (_) => onToggleSelection(path),
              )
              : const Icon(Icons.folder),
      title: Text(name),
      selected: isSelected,
      onTap:
          isSelectionMode
              ? () => onToggleSelection(path)
              : () => onNavigate(path),
      onLongPress: isSelectionMode ? null : () => onToggleSelection(path),
      trailing:
          isSelectionMode
              ? null
              : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  IconButton(
                    icon: const Icon(Icons.edit_outlined, size: 20),
                    tooltip: 'Rename',
                    onPressed: () => onRename(path, name),
                  ),
                  IconButton(
                    icon: const Icon(Icons.delete_outline, size: 20),
                    tooltip: 'Delete',
                    onPressed: () => onDelete(path, name),
                  ),
                ],
              ),
    );
  }

  Widget _buildFileTile(String name, String path, int size, bool isSelected) {
    return ListTile(
      leading:
          isSelectionMode
              ? Checkbox(
                value: isSelected,
                onChanged: (_) => onToggleSelection(path),
              )
              : const Icon(Icons.insert_drive_file_outlined),
      title: Text(name),
      subtitle: Text(_formatSize(size)),
      selected: isSelected,
      onTap: isSelectionMode ? () => onToggleSelection(path) : null,
      onLongPress: isSelectionMode ? null : () => onToggleSelection(path),
      trailing:
          isSelectionMode
              ? null
              : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  IconButton(
                    icon: const Icon(Icons.edit_outlined, size: 20),
                    tooltip: 'Rename',
                    onPressed: () => onRename(path, name),
                  ),
                  IconButton(
                    icon: const Icon(Icons.delete_outline, size: 20),
                    tooltip: 'Delete',
                    onPressed: () => onDelete(path, name),
                  ),
                ],
              ),
    );
  }

  static String _formatSize(int bytes) {
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
    if (bytes < 1024 * 1024 * 1024) {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
  }
}
