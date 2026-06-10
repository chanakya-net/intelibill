import 'dart:async';

import 'package:flutter/material.dart';

class InventorySpeedDialAction {
  const InventorySpeedDialAction({
    required this.label,
    required this.icon,
    required this.onTap,
    this.key,
  });

  final String label;
  final IconData icon;
  final VoidCallback onTap;
  final Key? key;
}

class InventorySpeedDial extends StatefulWidget {
  const InventorySpeedDial({
    required this.actions,
    super.key,
    this.mainFabKey,
  });

  final List<InventorySpeedDialAction> actions;
  final Key? mainFabKey;

  @override
  State<InventorySpeedDial> createState() => _InventorySpeedDialState();
}

class _InventorySpeedDialState extends State<InventorySpeedDial>
    with SingleTickerProviderStateMixin {
  static const _mainFabSize = 56.0;
  static const _childStep = 58.0;

  late final AnimationController _controller;
  late final Animation<double> _expandAnimation;
  bool _isOpen = false;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 250),
    );
    _expandAnimation = CurvedAnimation(
      parent: _controller,
      curve: Curves.easeOutCubic,
      reverseCurve: Curves.easeInCubic,
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _toggle() {
    setState(() {
      _isOpen = !_isOpen;
      if (_isOpen) {
        unawaited(_controller.forward());
      } else {
        unawaited(_controller.reverse());
      }
    });
  }

  void _close() {
    if (!_isOpen) return;
    setState(() => _isOpen = false);
    unawaited(_controller.reverse());
  }

  void _onActionTap(InventorySpeedDialAction action) {
    _close();
    action.onTap();
  }

  @override
  Widget build(BuildContext context) {
    final childCount = widget.actions.length;
    final height = _mainFabSize + 12 + (_childStep * childCount) + 16;

    return SizedBox(
      width: 220,
      height: height,
      child: Stack(
        clipBehavior: Clip.none,
        alignment: Alignment.bottomRight,
        children: [
          for (var index = 0; index < childCount; index++)
            _SpeedDialChild(
              action: widget.actions[index],
              index: index,
              animation: _expandAnimation,
              onTap: () => _onActionTap(widget.actions[index]),
            ),
          RotationTransition(
            turns: Tween<double>(
              begin: 0,
              end: 0.125,
            ).animate(_expandAnimation),
            child: FloatingActionButton(
              key: widget.mainFabKey,
              heroTag: 'inventory-speed-dial-main',
              onPressed: _toggle,
              child: AnimatedSwitcher(
                duration: const Duration(milliseconds: 200),
                child: Icon(
                  _isOpen ? Icons.close : Icons.add,
                  key: ValueKey<bool>(_isOpen),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _SpeedDialChild extends StatelessWidget {
  const _SpeedDialChild({
    required this.action,
    required this.index,
    required this.animation,
    required this.onTap,
  });

  final InventorySpeedDialAction action;
  final int index;
  final Animation<double> animation;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final stagger = 10.0 * index;

    return AnimatedBuilder(
      animation: animation,
      builder: (context, child) {
        final progress = animation.value;
        if (progress == 0) {
          return const SizedBox.shrink();
        }

        return Positioned(
          right: 4 + stagger * progress,
          bottom:
              _InventorySpeedDialState._mainFabSize +
              12 +
              (_InventorySpeedDialState._childStep * (index + 1) * progress),
          child: Transform.scale(
            scale: progress,
            child: Opacity(
              opacity: progress,
              child: child,
            ),
          ),
        );
      },
      child: Semantics(
        button: true,
        label: action.label,
        child: InkWell(
          key: action.key,
          onTap: onTap,
          borderRadius: BorderRadius.circular(28),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 6,
                ),
                decoration: BoxDecoration(
                  color: colorScheme.surface,
                  borderRadius: BorderRadius.circular(20),
                  boxShadow: const [
                    BoxShadow(
                      color: Color(0x1A7C2D12),
                      blurRadius: 8,
                      offset: Offset(0, 2),
                    ),
                  ],
                ),
                child: Text(
                  action.label,
                  style: theme.textTheme.labelLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                    color: colorScheme.onSurface,
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Material(
                elevation: 4,
                shadowColor: const Color(0x1A7C2D12),
                shape: const CircleBorder(),
                color: colorScheme.primaryContainer,
                child: SizedBox(
                  width: 48,
                  height: 48,
                  child: Icon(
                    action.icon,
                    color: colorScheme.onPrimaryContainer,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
