import { useMotionValue, useTransform, animate } from 'framer-motion';
import { useEffect, useState } from 'react';

interface AnimatedCounterProps {
    target: number;
    duration?: number;
    format?: (v: number) => string;
}

export function AnimatedCounter({ target, duration = 1.5, format }: AnimatedCounterProps) {
    const count = useMotionValue(0);
    const rounded = useTransform(count, (v) => Math.round(v));
    const [display, setDisplay] = useState(0);

    useEffect(() => {
        const unsub = rounded.on('change', (v) => setDisplay(v));
        return unsub;
    }, [rounded]);

    useEffect(() => {
        const controls = animate(count, target, { duration, ease: [0.22, 1, 0.36, 1] });
        return controls.stop;
    }, [target, count, duration]);

    return <>{format ? format(display) : display}</>;
}