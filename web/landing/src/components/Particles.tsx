import { motion } from 'framer-motion';
import { useMemo } from 'react';

interface ParticlesProps {
    count?: number;
}

export function Particles({ count = 14 }: ParticlesProps) {
    // Генерируем массив частиц с детерминированными случайными параметрами один раз
    const particles = useMemo(
        () =>
            Array.from({ length: count }, (_, i) => ({
                id: i,
                size: 1 + Math.random() * 2.5,
                left: Math.random() * 100,
                top: Math.random() * 100,
                delay: Math.random() * 10,
                duration: 14 + Math.random() * 10,
                moveX: (Math.random() - 0.5) * 60,
                moveY: -50 - Math.random() * 40,
                opacity: 0.2 + Math.random() * 0.5,
            })),
        [count]
    );

    return (
        <div className="absolute inset-0 overflow-hidden pointer-events-none">
            {particles.map((p) => (
                <motion.div
                    key={p.id}
                    className="absolute rounded-full will-change-transform"
                    style={{
                        width: p.size,
                        height: p.size,
                        left: `${p.left}%`,
                        top: `${p.top}%`,
                        background: '#E8E8E8',
                        boxShadow: '0 0 6px rgba(232,232,232,0.6)',
                        transform: 'translateZ(0)',
                    }}
                    animate={{
                        x: [0, p.moveX, 0],
                        y: [0, p.moveY, 0],
                        opacity: [0, p.opacity, 0],
                    }}
                    transition={{
                        duration: p.duration,
                        repeat: Infinity,
                        delay: p.delay,
                        ease: 'easeInOut',
                    }}
                />
            ))}
        </div>
    );
}