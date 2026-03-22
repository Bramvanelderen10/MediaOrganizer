import 'package:flutter/material.dart';
import '../../../services/api_service.dart';

class ForgetSeasonDialog extends StatefulWidget {
  final ApiService api;

  const ForgetSeasonDialog({super.key, required this.api});

  static Future<void> show(BuildContext context, ApiService api) {
    return showDialog<void>(
      context: context,
      builder: (_) => ForgetSeasonDialog(api: api),
    );
  }

  @override
  State<ForgetSeasonDialog> createState() => _ForgetSeasonDialogState();
}

class _ForgetSeasonDialogState extends State<ForgetSeasonDialog> {
  final _showController = TextEditingController();
  final _seasonController = TextEditingController();
  bool _isSubmitting = false;
  String? _errorText;

  @override
  void dispose() {
    _showController.dispose();
    _seasonController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final showName = _showController.text.trim();
    final seasonNumber = int.tryParse(_seasonController.text.trim());

    if (showName.isEmpty) {
      setState(() => _errorText = 'Show name is required.');
      return;
    }

    if (seasonNumber == null || seasonNumber <= 0) {
      setState(() => _errorText = 'Season number must be greater than 0.');
      return;
    }

    setState(() {
      _isSubmitting = true;
      _errorText = null;
    });

    try {
      await widget.api.forgetShowSeason(
        showName: showName,
        seasonNumber: seasonNumber,
      );

      if (!mounted) return;
      Navigator.pop(context);

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Forgot history for "$showName" season $seasonNumber.'),
          backgroundColor: Colors.green,
        ),
      );
    } catch (e) {
      setState(() {
        _isSubmitting = false;
        _errorText = 'Failed to forget season: $e';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Forget show season'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextField(
            controller: _showController,
            enabled: !_isSubmitting,
            textInputAction: TextInputAction.next,
            decoration: const InputDecoration(
              labelText: 'Show name',
              hintText: 'Sousou no Frieren',
            ),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _seasonController,
            enabled: !_isSubmitting,
            keyboardType: TextInputType.number,
            decoration: const InputDecoration(
              labelText: 'Season number',
              hintText: '2',
            ),
          ),
          if (_errorText != null) ...[
            const SizedBox(height: 10),
            Text(
              _errorText!,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: Theme.of(context).colorScheme.error,
              ),
            ),
          ],
        ],
      ),
      actions: [
        TextButton(
          onPressed: _isSubmitting ? null : () => Navigator.pop(context),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: _isSubmitting ? null : _submit,
          child:
              _isSubmitting
                  ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                  : const Text('Forget'),
        ),
      ],
    );
  }
}
