import 'package:flutter/material.dart';

class ApiStatusHeader extends StatelessWidget {
  final String apiUrl;

  const ApiStatusHeader({super.key, required this.apiUrl});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Icon(
          Icons.video_library_rounded,
          size: 80,
          color: Theme.of(context).colorScheme.primary,
        ),
        const SizedBox(height: 24),
        Text(
          'Connected to',
          textAlign: TextAlign.center,
          style: Theme.of(
            context,
          ).textTheme.bodyMedium?.copyWith(color: Colors.grey),
        ),
        Text(
          apiUrl,
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodyLarge,
        ),
      ],
    );
  }
}
