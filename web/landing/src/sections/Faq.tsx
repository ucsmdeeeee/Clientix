import { motion, AnimatePresence } from 'framer-motion';
import { Plus } from 'lucide-react';
import { useState } from 'react';

const PLATINUM = '#E8E8E8';

const faqs = [
    {
        q: 'Нужно ли мне знать программирование?',
        a: 'Нет. Всё настраивается в Telegram через кнопки за 5 минут. Не нужны сайты, домены или сложные интеграции.',
    },
    {
        q: 'Кто будет владельцем бота — я или ClientiX?',
        a: 'Только ты. Бот создаётся через @BotFather на твой Telegram-аккаунт. Мы храним только токен в зашифрованном виде, чтобы бот работал через нашу платформу.',
    },
    {
        q: 'Что если я отменю подписку?',
        a: 'Бот продолжит работать до конца оплаченного периода. После — записи в нём станут недоступны, но твой бот в @BotFather и все данные о клиентах останутся у тебя.',
    },
    {
        q: 'Можно ли подключить несколько ботов?',
        a: 'Сейчас один бот на одну подписку. Если у тебя несколько направлений, рекомендуем сделать одного бота с разными услугами — клиентам так удобнее.',
    },
    {
        q: 'Где хранятся данные клиентов?',
        a: 'На наших серверах в России. Никому не передаются. Резервная копия делается автоматически каждые 6 часов — в случае сбоя ничего не потеряется.',
    },
    {
        q: 'Какие способы оплаты доступны?',
        a: 'Оплата подписки через ЮKassa: карты любых банков, СБП. Чеки приходят автоматически.',
    },
    {
        q: 'Есть ли поддержка?',
        a: 'Да. Пиши в Telegram @ucsmdeeeee — отвечаем обычно в течение часа.',
    },
];

export function Faq() {
    const [open, setOpen] = useState<number | null>(0);
    const [hover, setHover] = useState<number | null>(null);

    return (
        <section
            className="relative py-32 md:py-40 overflow-hidden"
            style={{ background: '#000000' }}
        >
            <div className="relative z-10 max-w-3xl mx-auto px-6">
                <motion.div
                    initial={{ opacity: 0, y: 40 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, margin: '-100px' }}
                    transition={{ duration: 0.8 }}
                    className="text-center mb-16"
                >
                    <span
                        className="text-xs tracking-[0.3em] uppercase font-medium"
                        style={{ color: PLATINUM }}
                    >
                        Частые вопросы
                    </span>
                    <h2 className="font-serif text-4xl md:text-6xl font-light text-cx-cream mt-4 leading-tight">
                        Что важно
                        <br />
                        <span className="italic" style={{ color: PLATINUM }}>
                            знать
                        </span>
                    </h2>
                </motion.div>

                <div className="space-y-1">
                    {faqs.map((faq, index) => {
                        const isOpen = open === index;
                        const isHover = hover === index;

                        return (
                            <motion.div
                                key={index}
                                initial={{ opacity: 0, y: 20 }}
                                whileInView={{ opacity: 1, y: 0 }}
                                viewport={{ once: true, margin: '-50px' }}
                                transition={{ duration: 0.5, delay: index * 0.05 }}
                                onHoverStart={() => setHover(index)}
                                onHoverEnd={() => setHover(null)}
                                className="relative border-b transition-all duration-300"
                                style={{
                                    borderColor: isHover || isOpen ? PLATINUM : '#1F1F26',
                                }}
                            >
                                {/* Платиновая полоска слева при hover/open */}
                                <motion.div
                                    className="absolute left-0 top-0 w-px h-full"
                                    initial={{ scaleY: 0 }}
                                    animate={{ scaleY: isOpen || isHover ? 1 : 0 }}
                                    transition={{ duration: 0.4, ease: 'easeOut' }}
                                    style={{
                                        background: `linear-gradient(180deg, transparent 0%, ${PLATINUM} 50%, transparent 100%)`,
                                        transformOrigin: 'top',
                                    }}
                                />

                                <button
                                    onClick={() => setOpen(isOpen ? null : index)}
                                    className="w-full py-6 px-2 flex items-center justify-between gap-6 text-left group"
                                >
                                    <motion.span
                                        animate={{
                                            x: isHover && !isOpen ? 4 : 0,
                                            color: isOpen ? PLATINUM : '#F5F1E8',
                                        }}
                                        transition={{ duration: 0.3 }}
                                        className="font-serif text-xl md:text-2xl font-light"
                                    >
                                        {faq.q}
                                    </motion.span>

                                    <motion.div
                                        animate={{
                                            rotate: isOpen ? 45 : 0,
                                            scale: isHover ? 1.15 : 1,
                                        }}
                                        transition={{ duration: 0.3, ease: 'easeInOut' }}
                                        className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full"
                                        style={{
                                            background: isOpen ? 'rgba(232,232,232,0.1)' : 'transparent',
                                            border: `1px solid ${isOpen || isHover ? PLATINUM : 'rgba(232,232,232,0.2)'}`,
                                        }}
                                    >
                                        <Plus
                                            className="w-4 h-4"
                                            style={{ color: PLATINUM, strokeWidth: 1.5 }}
                                        />
                                    </motion.div>
                                </button>

                                <AnimatePresence initial={false}>
                                    {isOpen && (
                                        <motion.div
                                            initial={{ height: 0, opacity: 0 }}
                                            animate={{ height: 'auto', opacity: 1 }}
                                            exit={{ height: 0, opacity: 0 }}
                                            transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
                                            className="overflow-hidden"
                                        >
                                            <motion.p
                                                initial={{ y: -10 }}
                                                animate={{ y: 0 }}
                                                exit={{ y: -10 }}
                                                transition={{ duration: 0.4 }}
                                                className="pb-6 px-2 pr-12 text-base md:text-lg font-light leading-relaxed"
                                                style={{ color: '#A8A8A8' }}
                                            >
                                                {faq.a}
                                            </motion.p>
                                        </motion.div>
                                    )}
                                </AnimatePresence>
                            </motion.div>
                        );
                    })}
                </div>

                <motion.div
                    initial={{ opacity: 0, y: 20 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true }}
                    transition={{ duration: 0.6, delay: 0.3 }}
                    className="mt-16 text-center"
                >
                    <p className="text-sm font-light mb-4" style={{ color: '#A8A8A8' }}>
                        Остались вопросы?
                    </p>
                    <motion.a
                        href="https://t.me/ucsmdeeeee"
                        target="_blank"
                        rel="noopener"
                        whileHover={{ scale: 1.05 }}
                        className="inline-block text-base font-medium tracking-wide relative group"
                        style={{ color: PLATINUM }}
                    >
                        Напиши @ucsmdeeeee
                        <motion.span
                            className="absolute -bottom-1 left-0 w-full h-px"
                            style={{ background: PLATINUM }}
                            initial={{ scaleX: 0 }}
                            whileHover={{ scaleX: 1 }}
                            transition={{ duration: 0.3 }}
                        />
                    </motion.a>
                </motion.div>
            </div>
        </section>
    );
}