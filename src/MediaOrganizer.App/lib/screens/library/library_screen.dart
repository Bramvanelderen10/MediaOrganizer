import 'package:flutter/material.dart';
import '../../services/api_service.dart';

class LibraryScreen extends StatefulWidget {
  final ApiService api;

  const LibraryScreen({super.key, required this.api});

  @override
  State<LibraryScreen> createState() => _LibraryScreenState();
}

class _LibraryScreenState extends State<LibraryScreen> {
  late Future<Map<String, dynamic>> _libraryFuture;
  final Map<String, Map<String, dynamic>> _selectedItems = {};

  bool get _isSelectionMode => _selectedItems.isNotEmpty;

  @override
  void initState() {
    super.initState();
    _libraryFuture = widget.api.getLibrary();
  }

  void _refresh() {
    setState(() {
      _libraryFuture = widget.api.getLibrary();
      _selectedItems.clear();
    });
  }

  void _toggleSelection(String key, Map<String, dynamic> forgetItem) {
    setState(() {
      if (_selectedItems.containsKey(key)) {
        _selectedItems.remove(key);
      } else {
        _selectedItems[key] = forgetItem;
      }
    });
  }

  void _startSelection(String key, Map<String, dynamic> forgetItem) {
    setState(() {
      _selectedItems[key] = forgetItem;
    });
  }

  void _clearSelection() {
    setState(() {
      _selectedItems.clear();
    });
  }

  Future<void> _forgetSelected() async {
    final count = _selectedItems.length;
    final confirmed = await showDialog<bool>(
      context: context,
      builder:
          (ctx) => AlertDialog(
            title: const Text('Forget selected?'),
            content: Text(
              'Remove $count selected item${count != 1 ? 's' : ''} from the library history?',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(ctx).pop(false),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.of(ctx).pop(true),
                child: const Text('Forget'),
              ),
            ],
          ),
    );

    if (confirmed != true) return;

    try {
      final items = _selectedItems.values.toList();
      await widget.api.forgetBatch(items);
      _refresh();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Removed $count item${count != 1 ? 's' : ''} from library history.',
            ),
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Failed to forget: $e')));
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
                title: Text('${_selectedItems.length} selected'),
                actions: [
                  IconButton(
                    icon: const Icon(Icons.delete),
                    tooltip: 'Forget selected',
                    onPressed: _forgetSelected,
                  ),
                ],
              )
              : AppBar(
                title: const Text('Library'),
                actions: [
                  IconButton(
                    icon: const Icon(Icons.refresh),
                    tooltip: 'Refresh',
                    onPressed: _refresh,
                  ),
                ],
              ),
      body: FutureBuilder<Map<String, dynamic>>(
        future: _libraryFuture,
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
                      'Failed to load library:\n${snapshot.error}',
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
          final movies = data['movies'] as List<dynamic>? ?? [];
          final shows = data['shows'] as List<dynamic>? ?? [];

          if (movies.isEmpty && shows.isEmpty) {
            return const Center(child: Text('No media in library yet.'));
          }

          return _LibraryList(
            movies: movies,
            shows: shows,
            api: widget.api,
            onChanged: _refresh,
            isSelectionMode: _isSelectionMode,
            selectedKeys: _selectedItems.keys.toSet(),
            onToggleSelection: _toggleSelection,
            onStartSelection: _startSelection,
          );
        },
      ),
    );
  }
}

class _LibraryList extends StatelessWidget {
  final List<dynamic> movies;
  final List<dynamic> shows;
  final ApiService api;
  final VoidCallback onChanged;
  final bool isSelectionMode;
  final Set<String> selectedKeys;
  final void Function(String key, Map<String, dynamic> item) onToggleSelection;
  final void Function(String key, Map<String, dynamic> item) onStartSelection;

  const _LibraryList({
    required this.movies,
    required this.shows,
    required this.api,
    required this.onChanged,
    required this.isSelectionMode,
    required this.selectedKeys,
    required this.onToggleSelection,
    required this.onStartSelection,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return ListView(
      padding: const EdgeInsets.symmetric(vertical: 8),
      children: [
        if (shows.isNotEmpty) ...[
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
            child: Text(
              'Shows (${shows.length})',
              style: theme.textTheme.titleMedium,
            ),
          ),
          ...shows.map(
            (show) => _ShowTile(
              show: show,
              api: api,
              onChanged: onChanged,
              isSelectionMode: isSelectionMode,
              selectedKeys: selectedKeys,
              onToggleSelection: onToggleSelection,
              onStartSelection: onStartSelection,
            ),
          ),
        ],
        if (movies.isNotEmpty) ...[
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 4),
            child: Text(
              'Movies (${movies.length})',
              style: theme.textTheme.titleMedium,
            ),
          ),
          ...movies.map(
            (movie) => _MovieTile(
              movie: movie,
              api: api,
              onChanged: onChanged,
              isSelectionMode: isSelectionMode,
              isSelected: selectedKeys.contains('movie:${movie['name']}'),
              onToggleSelection: onToggleSelection,
              onStartSelection: onStartSelection,
            ),
          ),
        ],
      ],
    );
  }
}

class _MovieTile extends StatelessWidget {
  final dynamic movie;
  final ApiService api;
  final VoidCallback onChanged;
  final bool isSelectionMode;
  final bool isSelected;
  final void Function(String key, Map<String, dynamic> item) onToggleSelection;
  final void Function(String key, Map<String, dynamic> item) onStartSelection;

  const _MovieTile({
    required this.movie,
    required this.api,
    required this.onChanged,
    required this.isSelectionMode,
    required this.isSelected,
    required this.onToggleSelection,
    required this.onStartSelection,
  });

  @override
  Widget build(BuildContext context) {
    final name = movie['name'] as String? ?? 'Unknown';
    final targetPath = movie['targetPath'] as String? ?? '';
    final key = 'movie:$name';
    final forgetItem = <String, dynamic>{'type': 'movie', 'movieName': name};

    return ListTile(
      leading:
          isSelectionMode
              ? Checkbox(
                value: isSelected,
                onChanged: (_) => onToggleSelection(key, forgetItem),
              )
              : const Icon(Icons.movie_outlined),
      title: Text(name),
      subtitle: Text(targetPath, maxLines: 1, overflow: TextOverflow.ellipsis),
      selected: isSelected,
      onTap: isSelectionMode ? () => onToggleSelection(key, forgetItem) : null,
      onLongPress:
          isSelectionMode ? null : () => onStartSelection(key, forgetItem),
      trailing:
          isSelectionMode
              ? null
              : IconButton(
                icon: const Icon(Icons.delete_outline),
                tooltip: 'Forget movie',
                onPressed: () => _confirmForget(context, name),
              ),
    );
  }

  void _confirmForget(BuildContext context, String name) {
    _showForgetDialog(
      context,
      title: 'Forget movie?',
      content: 'Remove "$name" from the library history?',
      onConfirm: () => api.forgetMovie(movieName: name),
      onChanged: onChanged,
    );
  }
}

class _ShowTile extends StatefulWidget {
  final dynamic show;
  final ApiService api;
  final VoidCallback onChanged;
  final bool isSelectionMode;
  final Set<String> selectedKeys;
  final void Function(String key, Map<String, dynamic> item) onToggleSelection;
  final void Function(String key, Map<String, dynamic> item) onStartSelection;

  const _ShowTile({
    required this.show,
    required this.api,
    required this.onChanged,
    required this.isSelectionMode,
    required this.selectedKeys,
    required this.onToggleSelection,
    required this.onStartSelection,
  });

  @override
  State<_ShowTile> createState() => _ShowTileState();
}

class _ShowTileState extends State<_ShowTile> {
  bool _expanded = false;

  @override
  Widget build(BuildContext context) {
    final name = widget.show['name'] as String? ?? 'Unknown';
    final seasons = widget.show['seasons'] as List<dynamic>? ?? [];
    final totalEpisodes = seasons.fold<int>(
      0,
      (sum, s) => sum + ((s['episodes'] as List<dynamic>?)?.length ?? 0),
    );
    final key = 'show:$name';
    final isSelected = widget.selectedKeys.contains(key);
    final forgetItem = <String, dynamic>{'type': 'show', 'showName': name};

    return GestureDetector(
      onLongPress:
          widget.isSelectionMode
              ? null
              : () => widget.onStartSelection(key, forgetItem),
      child: ExpansionTile(
        leading:
            widget.isSelectionMode
                ? Checkbox(
                  value: isSelected,
                  onChanged: (_) => widget.onToggleSelection(key, forgetItem),
                )
                : const Icon(Icons.tv_outlined),
        title: Text(name),
        subtitle: Text(
          '${seasons.length} season${seasons.length != 1 ? 's' : ''}, '
          '$totalEpisodes episode${totalEpisodes != 1 ? 's' : ''}',
        ),
        trailing:
            widget.isSelectionMode
                ? Icon(_expanded ? Icons.expand_less : Icons.expand_more)
                : Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    IconButton(
                      icon: const Icon(Icons.delete_outline),
                      tooltip: 'Forget show',
                      onPressed: () => _confirmForget(context, name),
                    ),
                    Icon(_expanded ? Icons.expand_less : Icons.expand_more),
                  ],
                ),
        initiallyExpanded: _expanded,
        onExpansionChanged: (v) => setState(() => _expanded = v),
        children:
            seasons
                .map(
                  (season) => _SeasonTile(
                    season: season,
                    showName: name,
                    api: widget.api,
                    onChanged: widget.onChanged,
                    isSelectionMode: widget.isSelectionMode,
                    selectedKeys: widget.selectedKeys,
                    onToggleSelection: widget.onToggleSelection,
                    onStartSelection: widget.onStartSelection,
                  ),
                )
                .toList(),
      ),
    );
  }

  void _confirmForget(BuildContext context, String name) {
    _showForgetDialog(
      context,
      title: 'Forget show?',
      content:
          'Remove all seasons and episodes of "$name" from the library history?',
      onConfirm: () => widget.api.forgetShow(showName: name),
      onChanged: widget.onChanged,
    );
  }
}

class _SeasonTile extends StatelessWidget {
  final dynamic season;
  final String showName;
  final ApiService api;
  final VoidCallback onChanged;
  final bool isSelectionMode;
  final Set<String> selectedKeys;
  final void Function(String key, Map<String, dynamic> item) onToggleSelection;
  final void Function(String key, Map<String, dynamic> item) onStartSelection;

  const _SeasonTile({
    required this.season,
    required this.showName,
    required this.api,
    required this.onChanged,
    required this.isSelectionMode,
    required this.selectedKeys,
    required this.onToggleSelection,
    required this.onStartSelection,
  });

  @override
  Widget build(BuildContext context) {
    final seasonNumber = season['seasonNumber'] as int? ?? 0;
    final episodes = season['episodes'] as List<dynamic>? ?? [];
    final key = 'season:$showName:$seasonNumber';
    final isSelected = selectedKeys.contains(key);
    final forgetItem = <String, dynamic>{
      'type': 'season',
      'showName': showName,
      'seasonNumber': seasonNumber,
    };

    return ExpansionTile(
      tilePadding: const EdgeInsets.only(left: 32, right: 16),
      leading:
          isSelectionMode
              ? Checkbox(
                value: isSelected,
                onChanged: (_) => onToggleSelection(key, forgetItem),
              )
              : null,
      title: Text('Season $seasonNumber'),
      subtitle: Text(
        '${episodes.length} episode${episodes.length != 1 ? 's' : ''}',
      ),
      trailing:
          isSelectionMode
              ? null
              : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  IconButton(
                    icon: const Icon(Icons.delete_outline, size: 20),
                    tooltip: 'Forget season',
                    onPressed: () => _confirmForget(context, seasonNumber),
                  ),
                ],
              ),
      children:
          episodes.map((ep) {
            final epNum = ep['episodeNumber'] as int? ?? 0;
            final targetPath = ep['targetPath'] as String? ?? '';
            final epKey = 'episode:$showName:$seasonNumber:$epNum';
            final epSelected = selectedKeys.contains(epKey);
            final epForgetItem = <String, dynamic>{
              'type': 'episode',
              'showName': showName,
              'seasonNumber': seasonNumber,
              'episodeNumber': epNum,
            };

            return ListTile(
              contentPadding: const EdgeInsets.only(left: 64, right: 16),
              leading:
                  isSelectionMode
                      ? Checkbox(
                        value: epSelected,
                        onChanged:
                            (_) => onToggleSelection(epKey, epForgetItem),
                      )
                      : null,
              title: Text('Episode $epNum'),
              subtitle: Text(
                targetPath,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
              selected: epSelected,
              onTap:
                  isSelectionMode
                      ? () => onToggleSelection(epKey, epForgetItem)
                      : null,
              onLongPress:
                  isSelectionMode
                      ? null
                      : () => onStartSelection(epKey, epForgetItem),
              trailing:
                  isSelectionMode
                      ? null
                      : IconButton(
                        icon: const Icon(Icons.delete_outline, size: 20),
                        tooltip: 'Forget episode',
                        onPressed:
                            () => _confirmForgetEpisode(
                              context,
                              seasonNumber,
                              epNum,
                            ),
                      ),
            );
          }).toList(),
    );
  }

  void _confirmForget(BuildContext context, int seasonNumber) {
    _showForgetDialog(
      context,
      title: 'Forget season?',
      content:
          'Remove all episodes of "$showName" season $seasonNumber from the library history?',
      onConfirm:
          () => api.forgetShowSeason(
            showName: showName,
            seasonNumber: seasonNumber,
          ),
      onChanged: onChanged,
    );
  }

  void _confirmForgetEpisode(
    BuildContext context,
    int seasonNumber,
    int episodeNumber,
  ) {
    _showForgetDialog(
      context,
      title: 'Forget episode?',
      content:
          'Remove "$showName" S${seasonNumber.toString().padLeft(2, '0')}E${episodeNumber.toString().padLeft(2, '0')} from the library history?',
      onConfirm:
          () => api.forgetEpisode(
            showName: showName,
            seasonNumber: seasonNumber,
            episodeNumber: episodeNumber,
          ),
      onChanged: onChanged,
    );
  }
}

void _showForgetDialog(
  BuildContext context, {
  required String title,
  required String content,
  required Future<String> Function() onConfirm,
  required VoidCallback onChanged,
}) {
  showDialog<bool>(
    context: context,
    builder:
        (ctx) => AlertDialog(
          title: Text(title),
          content: Text(content),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(ctx).pop(true),
              child: const Text('Forget'),
            ),
          ],
        ),
  ).then((confirmed) async {
    if (confirmed != true) return;
    try {
      await onConfirm();
      onChanged();
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Removed from library history.')),
        );
      }
    } catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Failed to forget: $e')));
      }
    }
  });
}
