import 'package:flutter/material.dart';
import '../../services/api_service.dart';

/// Displays disk storage information for the media destination folder.
class StorageScreen extends StatefulWidget {
  final ApiService api;

  const StorageScreen({super.key, required this.api});

  @override
  State<StorageScreen> createState() => _StorageScreenState();
}

class _StorageScreenState extends State<StorageScreen> {
  bool _isLoading = true;
  String? _error;

  String _folder = '';
  int _totalBytes = 0;
  int _usedBytes = 0;
  int _freeBytes = 0;

  @override
  void initState() {
    super.initState();
    _fetchStorageInfo();
  }

  Future<void> _fetchStorageInfo() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      final data = await widget.api.getStorageInfo();
      if (!mounted) return;
      setState(() {
        _folder = data['folder'] as String? ?? '';
        _totalBytes = (data['totalBytes'] as num?)?.toInt() ?? 0;
        _usedBytes = (data['usedBytes'] as num?)?.toInt() ?? 0;
        _freeBytes = (data['freeBytes'] as num?)?.toInt() ?? 0;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.toString();
        _isLoading = false;
      });
    }
  }

  String _formatBytes(int bytes) {
    if (bytes <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    int unitIndex = 0;
    double size = bytes.toDouble();
    while (size >= 1024 && unitIndex < units.length - 1) {
      size /= 1024;
      unitIndex++;
    }
    return '${size.toStringAsFixed(2)} ${units[unitIndex]}';
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(title: const Text('Storage')),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 640),
            child:
                _isLoading
                    ? const Center(child: CircularProgressIndicator())
                    : _error != null
                    ? _buildError(context)
                    : _buildContent(context, colorScheme),
          ),
        ),
      ),
    );
  }

  Widget _buildError(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.error_outline,
            size: 64,
            color: Theme.of(context).colorScheme.error,
          ),
          const SizedBox(height: 16),
          Text(
            'Failed to load storage info',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          Text(
            _error!,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: Theme.of(context).colorScheme.error,
            ),
          ),
          const SizedBox(height: 24),
          FilledButton.icon(
            onPressed: _fetchStorageInfo,
            icon: const Icon(Icons.refresh),
            label: const Text('Retry'),
          ),
        ],
      ),
    );
  }

  Widget _buildContent(BuildContext context, ColorScheme colorScheme) {
    final usedFraction = _totalBytes > 0 ? _usedBytes / _totalBytes : 0.0;
    final freeFraction = _totalBytes > 0 ? _freeBytes / _totalBytes : 0.0;

    // Color the bar based on usage percentage.
    final barColor =
        usedFraction > 0.9
            ? colorScheme.error
            : usedFraction > 0.75
            ? Colors.orange
            : colorScheme.primary;

    return SingleChildScrollView(
      padding: const EdgeInsets.all(32),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Icon(Icons.storage_rounded, size: 80, color: colorScheme.primary),
          const SizedBox(height: 24),
          Text(
            'Destination Folder',
            textAlign: TextAlign.center,
            style: Theme.of(
              context,
            ).textTheme.bodyMedium?.copyWith(color: Colors.grey),
          ),
          Text(
            _folder,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyLarge,
          ),
          const SizedBox(height: 40),

          // Usage bar
          ClipRRect(
            borderRadius: BorderRadius.circular(8),
            child: LinearProgressIndicator(
              value: usedFraction.clamp(0.0, 1.0),
              minHeight: 24,
              backgroundColor: colorScheme.surfaceContainerHighest,
              color: barColor,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            '${(usedFraction * 100).toStringAsFixed(1)}% used',
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 32),

          // Detail cards
          _StorageTile(
            icon: Icons.pie_chart_rounded,
            label: 'Total',
            value: _formatBytes(_totalBytes),
            color: colorScheme.primary,
          ),
          const SizedBox(height: 12),
          _StorageTile(
            icon: Icons.folder_rounded,
            label: 'Used',
            value: _formatBytes(_usedBytes),
            color: barColor,
          ),
          const SizedBox(height: 12),
          _StorageTile(
            icon: Icons.inventory_2_rounded,
            label: 'Free',
            value: _formatBytes(_freeBytes),
            color: Colors.green,
          ),
          const SizedBox(height: 8),
          Text(
            '${(freeFraction * 100).toStringAsFixed(1)}% available',
            textAlign: TextAlign.center,
            style: Theme.of(
              context,
            ).textTheme.bodySmall?.copyWith(color: Colors.grey),
          ),
          const SizedBox(height: 24),
          Center(
            child: TextButton.icon(
              onPressed: _fetchStorageInfo,
              icon: const Icon(Icons.refresh),
              label: const Text('Refresh'),
            ),
          ),
        ],
      ),
    );
  }
}

class _StorageTile extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;
  final Color color;

  const _StorageTile({
    required this.icon,
    required this.label,
    required this.value,
    required this.color,
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      elevation: 0,
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        child: Row(
          children: [
            Icon(icon, color: color, size: 28),
            const SizedBox(width: 16),
            Expanded(
              child: Text(
                label,
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            Text(
              value,
              style: Theme.of(
                context,
              ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.bold),
            ),
          ],
        ),
      ),
    );
  }
}
