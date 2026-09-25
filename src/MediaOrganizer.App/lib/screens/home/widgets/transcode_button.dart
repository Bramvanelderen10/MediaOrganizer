import 'package:flutter/material.dart';

/// Secondary action on the home screen: asks the server to transcode every media file that
/// is not yet in the configured target codec.
class TranscodeButton extends StatelessWidget {
  final bool isLoading;
  final bool isApiHealthy;
  final bool isDisabled;
  final VoidCallback onPressed;

  const TranscodeButton({
    super.key,
    required this.isLoading,
    required this.isApiHealthy,
    this.isDisabled = false,
    required this.onPressed,
  });

  @override
  Widget build(BuildContext context) {
    final canTranscode = !isLoading && isApiHealthy && !isDisabled;

    return SizedBox(
      width: double.infinity,
      height: 56,
      child: OutlinedButton.icon(
        onPressed: canTranscode ? onPressed : null,
        icon:
            isLoading
                ? const SizedBox(
                  width: 20,
                  height: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
                : const Icon(Icons.transform_rounded),
        label: Text(
          isLoading ? 'Transcoding…' : 'Transcode videos',
          style: const TextStyle(fontSize: 18),
        ),
      ),
    );
  }
}