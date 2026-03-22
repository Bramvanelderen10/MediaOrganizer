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

  @override
  void initState() {
    super.initState();
    _libraryFuture = widget.api.getLibrary();
  }

  void _refresh() {
    setState(() {
      _libraryFuture = widget.api.getLibrary();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
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

  const _LibraryList({
    required this.movies,
    required this.shows,
    required this.api,
    required this.onChanged,
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
            (show) => _ShowTile(show: show, api: api, onChanged: onChanged),
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
            (movie) => _MovieTile(movie: movie, api: api, onChanged: onChanged),
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

  const _MovieTile({
    required this.movie,
    required this.api,
    required this.onChanged,
  });

  @override
  Widget build(BuildContext context) {
    final name = movie['name'] as String? ?? 'Unknown';
    final targetPath = movie['targetPath'] as String? ?? '';

    return ListTile(
      leading: const Icon(Icons.movie_outlined),
      title: Text(name),
      subtitle: Text(targetPath, maxLines: 1, overflow: TextOverflow.ellipsis),
      trailing: IconButton(
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

  const _ShowTile({
    required this.show,
    required this.api,
    required this.onChanged,
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

    return ExpansionTile(
      leading: const Icon(Icons.tv_outlined),
      title: Text(name),
      subtitle: Text(
        '${seasons.length} season${seasons.length != 1 ? 's' : ''}, '
        '$totalEpisodes episode${totalEpisodes != 1 ? 's' : ''}',
      ),
      trailing: Row(
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
                ),
              )
              .toList(),
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

  const _SeasonTile({
    required this.season,
    required this.showName,
    required this.api,
    required this.onChanged,
  });

  @override
  Widget build(BuildContext context) {
    final seasonNumber = season['seasonNumber'] as int? ?? 0;
    final episodes = season['episodes'] as List<dynamic>? ?? [];

    return ExpansionTile(
      tilePadding: const EdgeInsets.only(left: 32, right: 16),
      title: Text('Season $seasonNumber'),
      subtitle: Text(
        '${episodes.length} episode${episodes.length != 1 ? 's' : ''}',
      ),
      trailing: Row(
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

            return ListTile(
              contentPadding: const EdgeInsets.only(left: 64, right: 16),
              title: Text('Episode $epNum'),
              subtitle: Text(
                targetPath,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
              trailing: IconButton(
                icon: const Icon(Icons.delete_outline, size: 20),
                tooltip: 'Forget episode',
                onPressed:
                    () => _confirmForgetEpisode(context, seasonNumber, epNum),
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
