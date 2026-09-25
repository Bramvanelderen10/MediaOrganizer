import 'package:flutter/material.dart';

class OrganizeButton extends StatelessWidget {
  final bool isLoading;
  final bool isApiHealthy;
  final String? apiUnavailableMessage;
  final VoidCallback onPressed;

  const OrganizeButton({
    super.key,
    required this.isLoading,
    required this.isApiHealthy,
    this.apiUnavailableMessage,
    required this.onPressed,
  });

  @override
  Widget build(BuildContext context) {
    final canOrganize = !isLoading && isApiHealthy;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SizedBox(
          width: double.infinity,
          height: 56,
          child: FilledButton.icon(
            onPressed: canOrganize ? onPressed : null,
            icon:
                isLoading
                    ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Colors.white,
                      ),
                    )
                    : const Icon(Icons.play_arrow_rounded),
            label: Text(
              isLoading ? 'Organizing…' : 'Organize videos',
              style: const TextStyle(fontSize: 18),
            ),
          ),
        ),
        if (!isApiHealthy && !isLoading) ...[
          const SizedBox(height: 12),
          Text(
            apiUnavailableMessage ?? 'API not available.',
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
              color: Theme.of(context).colorScheme.error,
            ),
          ),
        ],
      ],
    );
  }
}
