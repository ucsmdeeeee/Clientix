import { motion } from 'framer-motion';

const PLATINUM = '#E8E8E8';

const steps = [
    {
        num: '01',
        title: 'Подключи бота',
        desc: 'Открой @cl1ent1x_bot, создай своего бота через @BotFather и привяжи токен. Пять минут.',
    },
    {
        num: '02',
        title: 'Заполни кабинет',
        desc: 'Добавь услуги, расписание, портфолио. Расскажи о себе. Все настройки прямо в Telegram.',
    },
    {
        num: '03',
        title: 'Дели бота с клиентами',
        desc: 'Отправь ссылку на бот в Instagram и историях. Клиенты записываются сами, ты только работаешь.',
    },
];

export function HowItWorks() {
    return (
        <section
            id="how"
            className="relative py-32 md:py-40 overflow-hidden"
            style={{ background: '#000000' }}
        >
            <div className="relative z-10 max-w-6xl mx-auto px-6">
                <motion.div
                    initial={{ opacity: 0, y: 40 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, margin: '-100px' }}
                    transition={{ duration: 0.8 }}
                    className="text-center mb-24"
                >
                    <span
                        className="text-xs tracking-[0.3em] uppercase font-medium"
                        style={{ color: PLATINUM }}
                    >
                        Как это работает
                    </span>
                    <h2 className="font-serif text-4xl md:text-6xl font-light text-cx-cream mt-4 leading-tight">
                        Три шага
                        <br />
                        <span className="italic" style={{ color: PLATINUM }}>
                            до первой записи
                        </span>
                    </h2>
                </motion.div>

                <div className="space-y-20 md:space-y-32">
                    {steps.map((step, index) => (
                        <motion.div
                            key={step.num}
                            initial={{ opacity: 0, y: 60 }}
                            whileInView={{ opacity: 1, y: 0 }}
                            viewport={{ once: true, margin: '-100px' }}
                            transition={{ duration: 0.9, ease: [0.22, 1, 0.36, 1] }}
                            className={`flex flex-col md:flex-row items-center gap-8 md:gap-20 ${index % 2 === 1 ? 'md:flex-row-reverse' : ''
                                }`}
                        >
                            <motion.div
                                initial={{ scale: 0.8 }}
                                whileInView={{ scale: 1 }}
                                viewport={{ once: true }}
                                transition={{ duration: 0.8, delay: 0.2 }}
                                className="flex-shrink-0 relative"
                            >
                                <div
                                    className="font-serif text-[10rem] md:text-[14rem] font-light italic"
                                    style={{
                                        background: `linear-gradient(135deg, ${PLATINUM} 0%, rgba(232,232,232,0.2) 100%)`,
                                        WebkitBackgroundClip: 'text',
                                        WebkitTextFillColor: 'transparent',
                                        backgroundClip: 'text',
                                        lineHeight: '1.2',
                                        paddingBottom: '0.15em',
                                    }}
                                >
                                    {step.num}
                                </div>
                            </motion.div>

                            <motion.div
                                initial={{ opacity: 0, x: index % 2 === 1 ? -40 : 40 }}
                                whileInView={{ opacity: 1, x: 0 }}
                                viewport={{ once: true }}
                                transition={{ duration: 0.9, delay: 0.3 }}
                                className="flex-1 text-center md:text-left"
                            >
                                <h3 className="font-serif text-3xl md:text-5xl font-light text-cx-cream mb-4 leading-tight">
                                    {step.title}
                                </h3>
                                <p className="text-lg leading-relaxed font-light max-w-xl mx-auto md:mx-0" style={{ color: '#A8A8A8' }}>
                                    {step.desc}
                                </p>
                            </motion.div>
                        </motion.div>
                    ))}
                </div>
            </div>
        </section>
    );
}