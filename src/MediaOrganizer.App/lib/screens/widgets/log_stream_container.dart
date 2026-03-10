import 'package:flutter/material.dart';

class LogStreamContainer extends StatelessWidget {
  final bool isApiHealthy;
  final bool isLogConnecting;
  final bool isLogConnected;
  final List<String> logLines;
  final String? logError;
  final ScrollController scrollController;
  final VoidCallback onConnect;
  final VoidCallback onDisconnect;

  const LogStreamContainer({
    super.key,
    required this.isApiHealthy,
    required this.isLogConnecting,
    required this.isLogConnected,
    required this.logLines,
    required this.logError,
    required this.scrollController,
    required this.onConnect,
    required this.onDisconnect,
  });

  @override
  Widget build(BuildContext context) {
    final canToggle = isApiHealthy && !isLogConnecting;
    final statusText = isLogConnecting
        ? 'Connecting…'
        : isLogConnected
            ? 'Connected'
            : 'Disconnected';

    return Card(
      elevation: 0,
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Text(
                  'Live logs',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(width: 12),
                Text(
                  statusText,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: isLogConnected
                            ? Colors.green
                            : Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                ),
                const Spacer(),
                if (isLogConnecting)
                  const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                IconButton(
                  tooltip: isLogConnected ? 'Disconnect logs' : 'Connect logs',
                  onPressed: canToggle
                      ? () {
                          if (isLogConnected) {
                            onDisconnect();
                          } else {
                            onConnect();
                          }
                        }
                      : null,
                  icon: Icon(
                    isLogConnected
                        ? Icons.stop_circle_outlined
                        : Icons.play_circle_outline,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Container(
              height: 240,
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: Colors.black,
                borderRadius: BorderRadius.circular(8),
              ),
              child: logLines.isEmpty
                  ? const Center(
                      child: Text(
                        'No logs yet…',
                        style: TextStyle(
                          color: Colors.white70,
                          fontFamily: 'monospace',
                          fontSize: 12,
                        ),
                      ),
                    )
                  : Scrollbar(
                      child: ListView.builder(
                        controller: scrollController,
                        itemCount: logLines.length,
                        itemBuilder: (ctx, i) => Text(
                          logLines[i],
                          style: const TextStyle(
                            color: Colors.white,
                            fontFamily: 'monospace',
                            fontSize: 12,
                            height: 1.25,
                          ),
                        ),
                      ),
                    ),
            ),
            if (logError != null) ...[
              const SizedBox(height: 8),
              Text(
                logError!,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: Theme.of(context).colorScheme.error,
                    ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
