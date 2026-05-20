import { motion, useMotionValue, useTransform, animate } from 'framer-motion';
import { Check, ArrowRight, Sparkles } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Particles } from '../components/Particles';

const PLATINUM = '#E8E8E8';
const PLATINUM_LIGHT = '#F5F5F5';

const features = [
    'Свой Telegram-бот для клиентов',
    'Неограниченное количество записей',
    'Каталог услуг и портфолио',
    'Расписание и календарь',
    'Перенос, отмена и доп. услуги',
    'Авто-напоминания клиентам',
    'Статистика по записям и доходу',
    'Управление прямо из Telegram',
    'Поддержка по любым вопросам',
];

// Анимированный счётчик
function AnimatedCounter({ target, inView }: { target: number; inView: boolean }) {
    const count = useMotionValue(0);
    const rounded = useTransform(count, (v) => Math.round(v));
    const [display, setDisplay] = useState(0);

    useEffect(() => {
        const unsub = rounded.on('change', (v) => setDisplay(v));
        return unsub;
    }, [rounded]);

    useEffect(() => {
        if (inView) {
            const controls = animate(count, target, {
                duration: 2,
                ease: [0.22, 1, 0.36, 1],
            });
            return controls.stop;
        }
    }, [inView, count, target]);

    return <>{display}</>;
}

export function Pricing() {
    const ref = useRef<HTMLDivElement>(null);
    const [inView, setInView] = useState(false);

    useEffect(() => {
        const observer = new IntersectionObserver(
            ([entry]) => {
                if (entry.isIntersecting && !inView) setInView(true);
            },
            { threshold: 0.3 }
        );
        if (ref.current) observer.observe(ref.current);
        return () => observer.disconnect();
    }, [inView]);

    return (
        <section
            className="relative py-32 md:py-40 overflow-hidden"
            style={{
                background: 'linear-gradient(180deg, #000000 0%, #0D0D10 50%, #000000 100%)',
            }}
        >
            <Particles count={12} />

            <motion.div
                className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[800px] h-[800px] rounded-full will-change-transform"
                style={{
                    background: 'radial-gradient(circle, rgba(232,232,232,0.06) 0%, rgba(232,232,232,0) 70%)',
                    transform: 'translate(-50%, -50%) translateZ(0)',
                }}
                animate={{ scale: [1, 1.1, 1] }}
                transition={{ duration: 10, repeat: Infinity, ease: 'easeInOut' }}
            />

            <div className="relative z-10 max-w-5xl mx-auto px-6">
                <motion.div
                    initial={{ opacity: 0, y: 40 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, margin: '-100px' }}
                    transition={{ duration: 0.8 }}
                    className="text-center mb-20"
                >
                    <span
                        className="text-xs tracking-[0.3em] uppercase font-medium"
                        style={{ color: PLATINUM }}
                    >
                        Тарифы
                    </span>
                    <h2 className="font-serif text-4xl md:text-6xl font-light text-cx-cream mt-4 leading-tight">
                        Один тариф —
                        <br />
                        <span className="italic" style={{ color: PLATINUM }}>
                            всё включено
                        </span>
                    </h2>
                    <p className="mt-6 text-lg font-light max-w-xl mx-auto" style={{ color: '#A8A8A8' }}>
                        Без скрытых лимитов, без процентов с записей.
                    </p>
                </motion.div>

                <motion.div
                    ref={ref}
                    initial={{ opacity: 0, y: 60 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, margin: '-100px' }}
                    transition={{ duration: 0.9, ease: [0.22, 1, 0.36, 1] }}
                    className="relative max-w-2xl mx-auto"
                >
                    {/* Платиновая рамка с анимацией */}
                    <motion.div
                        className="absolute -inset-px rounded-sm"
                        style={{
                            background: `linear-gradient(135deg, ${PLATINUM} 0%, transparent 50%, ${PLATINUM} 100%)`,
                            backgroundSize: '200% 200%',
                        }}
                        animate={{
                            backgroundPosition: ['0% 0%', '100% 100%', '0% 0%'],
                            opacity: [0.3, 0.5, 0.3],
                        }}
                        transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
                    />

                    <motion.div
                        whileHover={{
                            boxShadow: '0 0 60px rgba(232,232,232,0.15)',
                            borderColor: 'rgba(232,232,232,0.2)',
                        }}
                        transition={{ duration: 0.5 }}
                        className="relative p-10 md:p-14 border"
                        style={{
                            background: 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                            borderColor: '#1F1F26',
                            borderRadius: '2px',
                        }}
                    >
                        <div
                            className="text-center mb-8 pb-8 border-b"
                            style={{ borderColor: '#1F1F26' }}
                        >
                            <motion.div
                                initial={{ opacity: 0, scale: 0.8 }}
                                whileInView={{ opacity: 1, scale: 1 }}
                                viewport={{ once: true }}
                                transition={{ duration: 0.5, delay: 0.2 }}
                                className="mb-3 inline-flex items-center gap-2"
                            >
                                <Sparkles className="w-4 h-4" style={{ color: PLATINUM }} />
                                <span
                                    className="inline-block text-xs tracking-[0.2em] uppercase px-3 py-1"
                                    style={{
                                        color: '#000000',
                                        background: PLATINUM,
                                        borderRadius: '2px',
                                    }}
                                >
                                    Первый месяц 300 ₽
                                </span>
                            </motion.div>

                            <motion.div
                                initial={{ opacity: 0, y: 20 }}
                                whileInView={{ opacity: 1, y: 0 }}
                                viewport={{ once: true }}
                                transition={{ duration: 0.6, delay: 0.3 }}
                                className="flex items-baseline justify-center gap-3 mb-4"
                            >
                                <span
                                    className="font-serif text-7xl md:text-8xl font-light tabular-nums inline-block"
                                    style={{
                                        background: `linear-gradient(135deg, ${PLATINUM} 0%, ${PLATINUM_LIGHT} 100%)`,
                                        WebkitBackgroundClip: 'text',
                                        WebkitTextFillColor: 'transparent',
                                        backgroundClip: 'text',
                                        lineHeight: '1.15',
                                        paddingBottom: '0.1em',
                                    }}
                                >
                                    <AnimatedCounter target={500} inView={inView} />
                                </span>
                                <span className="text-2xl font-light" style={{ color: '#A8A8A8' }}>
                                    ₽ / месяц
                                </span>
                            </motion.div>

                            <p className="text-sm font-light" style={{ color: '#6B6B6B' }}>
                                После первого месяца. Отмена в любой момент.
                            </p>
                        </div>

                        <ul className="space-y-3 mb-10">
                            {features.map((feature, index) => (
                                <motion.li
                                    key={feature}
                                    initial={{ opacity: 0, x: -20 }}
                                    whileInView={{ opacity: 1, x: 0 }}
                                    viewport={{ once: true }}
                                    transition={{ duration: 0.4, delay: index * 0.05 }}
                                    whileHover={{ x: 6 }}
                                    className="flex items-start gap-3 cursor-default p-2 -mx-2 rounded transition-colors hover:bg-cx-iron/30"
                                >
                                    <motion.div
                                        whileHover={{
                                            scale: 1.2,
                                            boxShadow: `0 0 15px ${PLATINUM}`,
                                        }}
                                        transition={{ duration: 0.3 }}
                                        className="flex-shrink-0 w-5 h-5 rounded-full flex items-center justify-center mt-0.5"
                                        style={{
                                            background: 'rgba(232,232,232,0.1)',
                                            border: '1px solid rgba(232,232,232,0.2)',
                                        }}
                                    >
                                        <Check
                                            className="w-3 h-3"
                                            style={{ color: PLATINUM, strokeWidth: 2.5 }}
                                        />
                                    </motion.div>
                                    <span className="font-light" style={{ color: '#D4D4D4' }}>
                                        {feature}
                                    </span>
                                </motion.li>
                            ))}
                        </ul>

                        <motion.a
                            href="https://t.me/cl1ent1x_bot"
                            target="_blank"
                            rel="noopener"
                            whileHover={{
                                scale: 1.02,
                                boxShadow: '0 0 50px rgba(232,232,232,0.5)',
                            }}
                            whileTap={{ scale: 0.98 }}
                            className="group relative w-full flex items-center justify-center gap-3 px-10 py-4 font-medium tracking-wide overflow-hidden"
                            style={{
                                background: `linear-gradient(135deg, ${PLATINUM} 0%, ${PLATINUM_LIGHT} 100%)`,
                                color: '#000000',
                                borderRadius: '2px',
                            }}
                        >
                            {/* Блик пробегает при ховере */}
                            <motion.div
                                className="absolute inset-0 -translate-x-full group-hover:translate-x-full transition-transform duration-1000"
                                style={{
                                    background: 'linear-gradient(90deg, transparent, rgba(255,255,255,0.5), transparent)',
                                }}
                            />
                            <span className="relative z-10">Попробовать 7 дней бесплатно</span>
                            <ArrowRight className="relative z-10 w-4 h-4 group-hover:translate-x-1 transition-transform" />
                        </motion.a>

                        <p
                            className="text-center mt-4 text-xs tracking-wider uppercase"
                            style={{ color: '#6B6B6B' }}
                        >
                            Без привязки карты на пробный период
                        </p>
                    </motion.div>
                </motion.div>
            </div>
        </section>
    );
}