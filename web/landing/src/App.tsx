import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { Hero } from './sections/Hero';
import { Features } from './sections/Features';
import { HowItWorks } from './sections/HowItWorks';
import { Pricing } from './sections/Pricing';
import { Faq } from './sections/Faq';
import { Footer } from './sections/Footer';
import { PrivacyPage } from './pages/PrivacyPage';
import { OfferPage } from './pages/OfferPage';
import { ConsentPage } from './pages/ConsentPage';

function LandingHome() {
    return (
        <div className="min-h-screen bg-cx-black text-cx-silver">
            <Hero />
            <Features />
            <HowItWorks />
            <Pricing />
            <Faq />
            <Footer />
        </div>
    );
}

function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route path="/" element={<LandingHome />} />
                <Route path="/privacy" element={<PrivacyPage />} />
                <Route path="/offer" element={<OfferPage />} />
                <Route path="/consent" element={<ConsentPage />} />
            </Routes>
        </BrowserRouter>
    );
}

export default App;