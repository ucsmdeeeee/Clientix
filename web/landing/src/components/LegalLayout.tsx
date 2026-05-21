import { motion } from 'framer-motion';
import { Link } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { Particles } from './Particles';
import logo from '../assets/logo.jpg';

const PLATINUM = '#E8E8E8';

interface LegalLayoutProps {
    title: string;
    subtitle: string;
    children: React.ReactNode;
}

export function LegalLayout({ title, subtitle, children }: LegalLayoutProps) {
    return (
        <div
            className="relative min-h-screen overflow-hidden"
            style={{ background: 'radial-gradient(ellipse at top, #0D0D10 0%, #000000 70%)' }}
        >
            <Particles count={8} />

            {/* Header */}
            <header
                className="sticky top-0 z-50 border-b backdrop-blur-md"
                style={{ borderColor: '#1F1F26', background: 'rgba(0,0,0,0.85)' }}
            >
                <div className="max-w-4xl mx-auto px-6 py-4 flex items-center justify-between">
                    <Link to="/" className="flex items-center gap-3 group">
                        <img
                            src={logo}
                            alt="ClientiX"
                            className="w-10 h-10 rounded-full transition-shadow"
                            style={{ boxShadow: '0 0 15px rgba(232,232,232,0.2)' }}
                        />
                        <div>
                            <h1
                                className="font-serif text-xl font-light"
                                style={{
                                    background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                    WebkitBackgroundClip: 'text',
                                    WebkitTextFillColor: 'transparent',
                                    backgroundClip: 'text',
                                }}
                            >
                                ClientiX
                            </h1>
                            <p className="text-[10px] tracking-widest uppercase" style={{ color: '#6B6B6B' }}>
                                Юридическая информация
                            </p>
                        </div>
                    </Link>

                    <Link
                        to="/"
                        className="flex items-center gap-2 text-sm font-light transition-colors hover:text-cx-platinum"
                        style={{ color: '#A8A8A8' }}
                    >
                        <ArrowLeft className="w-4 h-4" />
                        <span className="hidden md:inline">На главную</span>
                    </Link>
                </div>
            </header>

            {/* Title */}
            <div className="relative z-10 max-w-4xl mx-auto px-6 pt-16 pb-12">
                <motion.div
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6 }}
                >
                    <h2 className="font-serif text-4xl md:text-5xl font-light text-cx-cream mb-3">
                        {title.split(' ').map((word, i, arr) =>
                            i === arr.length - 1 ? (
                                <span
                                    key={i}
                                    className="italic"
                                    style={{
                                        background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                        WebkitBackgroundClip: 'text',
                                        WebkitTextFillColor: 'transparent',
                                        backgroundClip: 'text',
                                    }}
                                >
                                    {word}
                                </span>
                            ) : (
                                <span key={i}>{word} </span>
                            )
                        )}
                    </h2>
                    <p className="text-base font-light" style={{ color: '#A8A8A8' }}>
                        {subtitle}
                    </p>
                </motion.div>
            </div>

            {/* Content */}
            <main className="relative z-10 max-w-4xl mx-auto px-6 pb-20">
                <motion.article
                    initial={{ opacity: 0, y: 30 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6, delay: 0.2 }}
                    className="relative p-8 md:p-12 border legal-content"
                    style={{
                        background: 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                        borderColor: '#1F1F26',
                        borderRadius: '2px',
                    }}
                >
                    {children}
                </motion.article>

                <motion.div
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    transition={{ duration: 0.6, delay: 0.5 }}
                    className="mt-8 text-center text-xs tracking-wider uppercase"
                    style={{ color: '#6B6B6B' }}
                >
                    © {new Date().getFullYear()} ClientiX. Все права защищены.
                </motion.div>
            </main>

            <style>{`
                .legal-content {
                    color: #D4D4D4;
                    font-weight: 300;
                    line-height: 1.75;
                    font-size: 15px;
                }
                .legal-content h2 {
                    font-family: 'Playfair Display', serif;
                    font-size: 1.75rem;
                    font-weight: 300;
                    margin-top: 2.5rem;
                    margin-bottom: 1rem;
                    color: #E8E8E8;
                    padding-bottom: 0.5rem;
                    border-bottom: 1px solid #1F1F26;
                }
                .legal-content h2:first-child {
                    margin-top: 0;
                }
                .legal-content h3 {
                    font-family: 'Playfair Display', serif;
                    font-size: 1.25rem;
                    font-weight: 400;
                    margin-top: 1.5rem;
                    margin-bottom: 0.75rem;
                    color: #E8E8E8;
                }
                .legal-content p {
                    margin: 1rem 0;
                }
                .legal-content ul {
                    margin: 1rem 0;
                    padding-left: 1.5rem;
                    list-style: none;
                }
                .legal-content ul li {
                    position: relative;
                    margin: 0.5rem 0;
                    padding-left: 1rem;
                }
                .legal-content ul li:before {
                    content: '—';
                    position: absolute;
                    left: -0.5rem;
                    color: #6B6B6B;
                }
                .legal-content strong {
                    color: #E8E8E8;
                    font-weight: 500;
                }
                .legal-content a {
                    color: #E8E8E8;
                    text-decoration: underline;
                    text-decoration-color: rgba(232,232,232,0.3);
                    text-underline-offset: 3px;
                    transition: text-decoration-color 0.2s;
                }
                .legal-content a:hover {
                    text-decoration-color: rgba(232,232,232,0.8);
                }
                .legal-content .meta {
                    color: #6B6B6B;
                    font-size: 13px;
                    font-style: italic;
                    margin-bottom: 1.5rem;
                }
                .legal-content .reqs {
                    margin-top: 2rem;
                    padding: 1.5rem;
                    border: 1px solid #1F1F26;
                    border-radius: 2px;
                    background: rgba(0,0,0,0.4);
                    font-size: 14px;
                }
                .legal-content .reqs strong {
                    display: inline-block;
                    min-width: 140px;
                    color: #A8A8A8;
                    font-weight: 400;
                }
            `}</style>
        </div>
    );
}