import 'package:flutter/material.dart';

class PlaceholderPage extends StatelessWidget {
  const PlaceholderPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Intelibill'),
      ),
      body: const Center(
        child: Text('App Shell - Status page coming soon'),
      ),
    );
  }
}
