import { motion } from 'framer-motion';

interface SplitTextProps {
    text: string;
    className?: string;
    style?: React.CSSProperties;
    delay?: number;
}

export function SplitText({ text, className, style, delay = 0 }: SplitTextProps) {
    // Разбиваем на слова, чтобы переносы строк работали корректно
    const words = text.split(' ');

    let charIndex = 0;

    return (
        <span className={className} style={style}>
            {words.map((word, wordIdx) => (
                <span key={wordIdx} className="inline-block whitespace-nowrap">
                    {word.split('').map((char) => {
                        const i = charIndex++;
                        return (
                            <motion.span
                                key={i}
                                className="inline-block will-change-transform"
                                style={{ transform: 'translateZ(0)' }}
                                initial={{ opacity: 0, y: 40, rotateX: -90 }}
                                animate={{ opacity: 1, y: 0, rotateX: 0 }}
                                transition={{
                                    duration: 0.6,
                                    delay: delay + i * 0.03,
                                    ease: [0.22, 1, 0.36, 1],
                                }}
                            >
                                {char}
                            </motion.span>
                        );
                    })}
                    {wordIdx < words.length - 1 && (
                        <span className="inline-block">&nbsp;</span>
                    )}
                </span>
            ))}
        </span>
    );
}